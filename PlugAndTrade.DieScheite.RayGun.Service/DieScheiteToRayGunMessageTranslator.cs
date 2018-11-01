using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using Elmah;
using Mindscape.Raygun4Net;
using Newtonsoft.Json;
using PlugAndTrade.DieScheite.Client.Common;

namespace PlugAndTrade.DieScheite.RayGun.Service
{
    public class DieScheiteToRayGunMessageTranslator
    {
        private readonly int _minLevel;

        public DieScheiteToRayGunMessageTranslator(int minLevel)
        {
            _minLevel = minLevel;
        }

        public RaygunMessage Translate(LogEntry logEntry)
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

        private RaygunErrorMessage GetRaygunError(LogEntry logEntry)
        {
            var messages = logEntry.Messages.Where(m => m.Level >= _minLevel).ToArray();

            if (messages.Length == 0)
            {
                // If this happens, RabbitMQ binding and config MIN_LEVEL differs -> use message with highest severity
                return CreateRaygunErrorMessage(messages.MaxBy(m => m.Level));
            }

            if (messages.Length == 1)
            {
                var message = messages.Single();
                return CreateRaygunErrorMessage(message);
            }

            var highestSeverityMessage = messages.MaxBy(m => m.Level);
            return new RaygunErrorMessage
            {
                InnerErrors = messages.Select(CreateRaygunErrorMessage).ToArray(),
                Message = highestSeverityMessage.Message
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
            try
            {
                return StackTraceParser.Parse(
                    stacktrace,
                    (idx, len, txt) => new { Index = idx, Length = len, Text = txt },
                    (type, method) => new { Type = type, Method = method },
                    (type, name) => new { Type = type, Name = name },
                    (pl, ps) => new { List = pl, Parameters = ps },
                    (file, line) => new { File = file, Line = line },
                    (f, tm, p, fl) => new RaygunErrorStackTraceLineMessage
                    {
                        FileName = fl.File.Text,
                        ClassName = tm.Type.Text,
                        MethodName = tm.Method.Text,
                        LineNumber = int.TryParse(fl.Line.Text, out var lineNo) ? lineNo : 0
                    })
                    .ToArray();
            }
            catch
            {
                return new RaygunErrorStackTraceLineMessage[0];
            }
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
                if (!string.IsNullOrEmpty(header.Key))
                {
                    customData.Add(header.Key, JsonConvert.ToString(header.Value));
                }
            }

            return customData;
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