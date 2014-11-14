using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Stapler.UnityServer
{
    [InitializeOnLoad]
    class CommandListener
    {
        private static readonly HttpListener Listener = new HttpListener();
        private static readonly Queue<string> InvokeQueue = new Queue<string>();
        private static readonly List<LogEntry> LogMessages = new List<LogEntry>(); 
        private static readonly AutoResetEvent Evt = new AutoResetEvent(false);
        private struct LogEntry
        {
            public string Condition;
            public string Stacktrace;
            public LogType Type;

            public override string ToString()
            {
                return string.Format("{0}: {1}", Type, Condition);
            }
        }
        static CommandListener()
        {
            StartServer();
            EditorApplication.update += ProcessWorkQueue;
        }

        static void ProcessWorkQueue()
        {
            lock (InvokeQueue)
            {
                if (InvokeQueue.Count > 0)
                {
                    var method = InvokeQueue.Dequeue();

                    Application.RegisterLogCallback(HandleLog);
                    InvokeMethodFromName(method);
                    Application.RegisterLogCallback(null);

                    Evt.Set();
                }
            }
        }

        private static void HandleLog(string condition, string stacktrace, LogType type)
        {
            LogMessages.Add(new LogEntry { Condition = condition, Stacktrace = stacktrace, Type = type });
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        private static void StartServer()
        {
            var plainText = Path.GetDirectoryName(Application.dataPath);
            string prefix = string.Format("http://*:13711/{0}/", Base64Encode(plainText));
            Debug.Log(string.Format("Starting Listener: {0} for \"{1}\"", prefix, plainText));
            Listener.Prefixes.Add(prefix);
            Listener.Start();

            ThreadPool.QueueUserWorkItem(o =>
                {
                    while (Listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem(HandleRequest, Listener.GetContext());
                    }
                });
        }

        private static void HandleRequest(object listenerContext)
        {
            var ctx = listenerContext as HttpListenerContext;
            if (ctx == null) return;

            var response = ctx.Response;
            using (var output = response.OutputStream)
            {
                switch (ctx.Request.HttpMethod)
                {
                    case "GET":
                        const string responseString1 = "<html><body>Running</body></html>"; // TODO: unity settings
                        WriteResponseString(response, output, responseString1);
                        break;
                    case "POST":
                        var istream = ctx.Request.InputStream;
                        using (var read = new StreamReader(istream))
                        {
                            var fullyQualifiedTypeAndMethodName = read.ReadToEnd();
                            lock (InvokeQueue)
                            {
                                InvokeQueue.Enqueue(fullyQualifiedTypeAndMethodName);
                            }
                        }
                        Evt.WaitOne();
                        string responseString = "<html><body>" +
                            string.Join("\n", LogMessages.Select(m => m.ToString()).ToArray())
                            +"</body></html>";
                        WriteResponseString(response, output, responseString);
                        break;
                }
            }
        }

        private static void InvokeMethodFromName(string fullyQualifiedTypeAndMethodName)
        {
            Debug.Log(string.Format("Invoking {0}... ", fullyQualifiedTypeAndMethodName));
            var parts = fullyQualifiedTypeAndMethodName.Split('.');
            var typeName = string.Join(".", parts.Take(parts.Length - 1).ToArray());

            var type = (from asm in AppDomain.CurrentDomain.GetAssemblies()
                        let ype = asm.GetType(typeName)
                        where ype != null
                        select ype).SingleOrDefault();
            if (type != null)
            {
                var methodName = parts.Last();
                var method = type.GetMethod(methodName);
                try
                {
                    method.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(string.Format("Failed to invoke {0}. Exception thrown.", fullyQualifiedTypeAndMethodName));
                    Debug.LogException(ex);
                }
            }
            else
            {
                Debug.LogWarning(string.Format("Failed to find method named {0}", fullyQualifiedTypeAndMethodName));
            }
        }

        private static void WriteResponseString(HttpListenerResponse response, Stream output, string responseString)
        {
            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            output.Write(buffer, 0, buffer.Length);
        }
    }
}
