using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
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
                m => OnMessage(raygunClient, serviceProvider.GetService<DieScheiteToRayGunMessageTranslator>(), m));

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
            services.AddSingleton<DieScheiteToRayGunMessageTranslator>();

            return services.BuildServiceProvider();
        }

        private static bool OnMessage(RaygunClient raygunClient, DieScheiteToRayGunMessageTranslator translator, Message message)
        {
            try
            {
                var logEntry = ReadLogEntry(message);
                if (logEntry.Level < (int)LogEntryLevel.Warning)
                {
                    return true;
                }
                var raygunMessage = translator.Translate(logEntry);
                var task = raygunClient.Send(raygunMessage);
                if (!task.Wait(TimeSpan.FromSeconds(60)))
                {
                    Console.WriteLine("[Raygun] :: sending timeout exceded");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
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

        private static Stream DecodePayload(Message arg)
        {
            var stream = new MemoryStream(arg.Data);
            if (string.Equals(arg.GetHeaderValue("Content-Encoding"), "gzip", StringComparison.InvariantCultureIgnoreCase))
            {
                return new GZipStream(stream, CompressionMode.Decompress);
            }

            return stream;
        }
    }
}
