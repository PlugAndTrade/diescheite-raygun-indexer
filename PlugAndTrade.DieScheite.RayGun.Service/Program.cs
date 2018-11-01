using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Mindscape.Raygun4Net;
using Newtonsoft.Json;
using PlugAndTrade.DieScheite.Client.Common;
using PlugAndTrade.RabbitMQ;

namespace PlugAndTrade.DieScheite.RayGun.Service
{
    class Program
    {
        static void Main(string[] args)
        {
            var serviceProvider = SetupContainer();

            var raygunClient = serviceProvider.GetService<RaygunClient>();

            var consumer = serviceProvider.GetService<RabbitMQClientFactory>().CreateQueueConsumer(
		Environment.GetEnvironmentVariable("RABBITMQ_QUEUE_NAME"),
		1,
 		m => OnMessage(raygunClient, m));
            consumer.Start();

            WaitForExit(() =>
            {
                Console.WriteLine("Stopping service...");
                consumer.Stop();
                serviceProvider.Dispose();
                Console.WriteLine("Service stopped!");
            });
        }

        private static void WaitForExit(Action cleanup = null)
        {
            var shutdown = new ManualResetEvent(false);
            System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += ctx => { shutdown.Set(); };
            if (Environment.UserInteractive)
            {
                Console.WriteLine("Service started! Press CTRL+C to exit.");
            }
            shutdown.WaitOne();
            cleanup?.Invoke();
        }

        private static ServiceProvider SetupContainer()
        {
            var services = new ServiceCollection();

            services.AddSingleton(c => new RabbitMQClientFactory(
                Environment.GetEnvironmentVariable("RABBITMQ_HOST"),
                int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672"),
                Environment.GetEnvironmentVariable("RABBITMQ_CONNECTIONNAME")));

            services.AddSingleton(c => new RaygunClient(Environment.GetEnvironmentVariable("RAYGUN_API_KEY")));

            return services.BuildServiceProvider();
        }

        private static bool OnMessage(RaygunClient raygunClient, Message message)
        {
            try
            {
                var logEntry = ReadLogEntry(message);
                if (logEntry.Level < (int) LogEntryLevel.Warning)
                {
                    return true;
                }
                var raygunMessage = CreateRaygunMessage(logEntry);
                var task = raygunClient.Send(raygunMessage);
                if (!task.Wait(TimeSpan.FromSeconds(60)))
                {
                    Console.WriteLine("[Raygun] :: sending timeout exceded");
                    return false;
                }

                return true;
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
        }

        private static RaygunMessage CreateRaygunMessage(LogEntry logEntry)
        {
            return new RaygunMessage
            {
                Details = new RaygunMessageDetails
                {
                    Client = new RaygunClientMessage
                    {
                        Name = logEntry.ServiceId,
                        Version = logEntry.ServiceVersion
                    },
                    UserCustomData = GetUserCustomData(logEntry),
                    Error = GetRaygunError(logEntry),
                    Request = CreateRaygunRequestMessage(logEntry),
                    Response = CreateRaygunResponseMessage(logEntry)
                }
            };
        }

        private static RaygunResponseMessage CreateRaygunResponseMessage(LogEntry logEntry)
        {
            if (logEntry.Http?.Response == null)
            {
                return null;
            }

            return new RaygunResponseMessage
            {
                Content = GetTextContent(logEntry.Http.Response.Body),
                StatusCode = logEntry.Http.Response.StatusCode ?? 0
            };
        }

        private static RaygunRequestMessage CreateRaygunRequestMessage(LogEntry logEntry)
        {
            if (logEntry.Http?.Request == null)
            {
                return null;
            }

            var requestDetails = new RaygunRequestMessage
            {
                HttpMethod = logEntry.Http.Request.Method,
                Url = logEntry.Http.Request.Uri,
                HostName = logEntry.Http.Request.Host,
                RawData = GetTextContent(logEntry.Http.Request.Body),
                Headers = logEntry.Http.Request.Headers?.Where(h => !string.IsNullOrEmpty(h.Key)).ToLookup(h => h.Key, h => h.Value).ToDictionary(g => g.Key, g => g.First())
            };

            // TODO Uri.Query only works for absolute urls, wtf?
            if (logEntry.Http.Request.Uri.Contains('?'))
            {
                var queryString = HttpUtility.ParseQueryString(logEntry.Http.Request.Uri.Split('?').Last());
                var applicableKeys = queryString.AllKeys.Where(s => !string.IsNullOrEmpty(s));

                requestDetails.QueryString = applicableKeys.ToDictionary(k => k, k => queryString[k]);
            }

            return requestDetails;
        }

        private static RaygunErrorMessage GetRaygunError(LogEntry logEntry)
        {
            if (logEntry.Messages.Count == 1)
            {
                var message = logEntry.Messages.Single();
                return CreateRaygunErrorMessage(message);
            }

            return new RaygunErrorMessage
            {
                InnerErrors = logEntry.Messages.Select(CreateRaygunErrorMessage).ToArray(),
                Message = "Multiple errors"
            };
        }

        private static RaygunErrorMessage CreateRaygunErrorMessage(LogEntryMessage message)
        {
            return new RaygunErrorMessage
            {
                Message = message.Message,
                StackTrace = CreateStackTrace(message.Stacktrace),
                Data = GetRaygunErrorMessageData(message)
            };
        }

        private static Dictionary<string, string> GetRaygunErrorMessageData(LogEntryMessage message)
        {
            var data = new Dictionary<string, string>
            {
                {"level", Convert.ToString(message.Level)}
            };

            if (message.Attachments != null && message.Attachments.Any())
            {
                data.Add("attachmentIds", string.Join(",", message.Attachments.Select(a => a.Id)));
            }

            if (!string.IsNullOrEmpty(message.TraceId))
            {
                data.Add("traceId", message.TraceId);
            }

            return data;
        }

        private static RaygunErrorStackTraceLineMessage[] CreateStackTrace(string stacktrace)
        {
            return new RaygunErrorStackTraceLineMessage[0];
        }

        private static Dictionary<string, string> GetUserCustomData(LogEntry logEntry)
        {
            var customData = new Dictionary<string, string>
            {
                { "id", logEntry.Id },
                { "correlationId", logEntry.CorrelationId },
                { "duration", Convert.ToString(logEntry.Duration) },
            };

            if (!string.IsNullOrEmpty(logEntry.ParentId))
            {
                customData.Add("parentId", logEntry.ParentId);
            }

            foreach (var header in logEntry.Headers ?? new List<KeyValuePair<string, object>>())
            {
                if (!string.IsNullOrEmpty(header.Key)) {
                    customData.Add(header.Key, JsonConvert.ToString(header.Value));
                }
            }

            return customData;
        }

        private static LogEntry ReadLogEntry(Message arg)
        {
            using (var stream = DecodePayload(arg))
            using (var streamReader = new StreamReader(stream, Encoding.UTF8))
            {
                var json = streamReader.ReadToEnd();
                return JsonConvert.DeserializeObject<LogEntry>(json);
            }
        }

        private static Stream DecodePayload(Message arg) {
            var stream = new MemoryStream(arg.Data);
            if (arg.GetHeaderValue("Content-Encoding") == "gzip") {
                return new GZipStream(stream, CompressionMode.Decompress);
            }

            return stream;
        }

        private static string GetTextContent(byte[] body)
        {
            try
            {
                return Encoding.UTF8.GetString(body);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
