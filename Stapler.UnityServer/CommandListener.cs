using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Stapler.UnityServer
{
    [InitializeOnLoad]
    internal class CommandListener
    {
        private static HttpListener listener;
        private static readonly Queue<AsyncCommand> COMMAND_QUEUE = new Queue<AsyncCommand>();
        private static bool quitWhenQueueEmpty;
        private static readonly DateTime SERVICE_START_TIME;

        static CommandListener()
        {
            SERVICE_START_TIME = DateTime.UtcNow;
            EditorApplication.update += ProcessWorkQueue;
        }

        public static void Quit()
        {
            quitWhenQueueEmpty = InternalEditorUtility.inBatchMode;
        }

        public static void LogTest()
        {
            Debug.Log("Level: 'LogMessage'");
            Debug.LogWarning("Level: 'Warning'");
            Debug.LogError("Level: 'Error'");
        }

        public static void ThrowTest()
        {
            throw new Exception("Test exception");
        }

        private static void ProcessWorkQueue()
        {
            if (listener == null) // Wait to initialize the listener so that we're loaded?
            {
                listener = new HttpListener();
                StartServer();
            }

            AsyncCommand command = null;
            lock (COMMAND_QUEUE)
            {
                if (COMMAND_QUEUE.Count > 0)
                {
                    command = COMMAND_QUEUE.Dequeue();
                }
            }

            if (command != null)
            {
                InvokeWithLogHandler(command);
                command.CommandProcessed.Set();
            }

            lock (COMMAND_QUEUE)
            {
                if (COMMAND_QUEUE.Count == 0 && quitWhenQueueEmpty)
                {
                    EditorApplication.Exit(0);
                }
            }
        }

        private static void InvokeWithLogHandler(AsyncCommand command)
        {
            command.LogMessages.Clear();
            Application.RegisterLogCallback((condition, stacktrace, logType) => HandleLog(command, condition, logType));
            bool success = InvokeMethodFromName(command.Method);
            Application.RegisterLogCallback(null);
            command.ResultCode = success && !LogHasErrors(command) ? 200 : 500;
        }

        private static bool LogHasErrors(AsyncCommand command)
        {
            return command.LogMessages.Any(e => e.Type == LogType.Error || e.Type == LogType.Assert || e.Type == LogType.Exception);
        }

        private static void HandleLog(AsyncCommand command, string condition, LogType type)
        {
            command.LogMessages.Add(new LogEntry { Condition = condition, Type = type });
        }

        public static string Base64Encode(string plainText)
        {
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        private static void StartServer()
        {
            string plainText = Path.GetDirectoryName(Application.dataPath);
            string prefix = string.Format("http://*:13711/{0}/", Base64Encode(plainText));
            Debug.Log(string.Format("Starting Listener: {0} for \"{1}\"", prefix, plainText));
            listener.Prefixes.Add(prefix);
            listener.Start();

            ThreadPool.QueueUserWorkItem(o =>
            {
                while (listener.IsListening)
                {
                    ThreadPool.QueueUserWorkItem(HandleRequest, listener.GetContext());
                }
            });
        }

        private static void HandleRequest(object listenerContext)
        {
            var ctx = listenerContext as HttpListenerContext;
            if (ctx == null)
            {
                return;
            }

            HttpListenerResponse response = ctx.Response;

            using (Stream output = response.OutputStream)
            {
                switch (ctx.Request.HttpMethod)
                {
                    case "GET":
                        HandleGet(response, output);
                        break;
                    case "POST":
                        HandlePost(ctx, response, output);
                        break;
                }
            }
        }

        private static void HandleGet(HttpListenerResponse response, Stream output)
        {
            string statusResponse = string.Format("<!doctype html><html lang=\"en\"><body><h1>Response Status:</h1><p/>Running: {0}s</body></html>", (DateTime.UtcNow - SERVICE_START_TIME).TotalSeconds); // TODO: unity settings
            WriteResponseString(response, output, statusResponse);
        }

        private static void HandlePost(HttpListenerContext ctx, HttpListenerResponse response, Stream output)
        {
            AsyncCommand command = CreateCommandFromRequest(ctx);
            lock (COMMAND_QUEUE)
            {
                COMMAND_QUEUE.Enqueue(command);
            }
            command.CommandProcessed.WaitOne();
            ctx.Response.StatusCode = command.ResultCode;
            WriteResponseString(response, output, command.GetResultBody());
        }

        private static AsyncCommand CreateCommandFromRequest(HttpListenerContext ctx)
        {
            Stream istream = ctx.Request.InputStream;
            using (var read = new StreamReader(istream))
            {
                string fullyQualifiedTypeAndMethodName = read.ReadToEnd();
                return new AsyncCommand(fullyQualifiedTypeAndMethodName);
            }
        }

        private static bool InvokeMethodFromName(string fullyQualifiedTypeAndMethodName)
        {
            Debug.Log(string.Format("Invoking {0}... ", fullyQualifiedTypeAndMethodName));
            string[] parts = fullyQualifiedTypeAndMethodName.Split('.');
            string typeName = string.Join(".", parts.Take(parts.Length - 1).ToArray());

            Type type = GetTypeFromLoadedAssemblies(typeName);
            if (type != null)
            {
                return InvokeMethod(fullyQualifiedTypeAndMethodName, parts, type);
            }
            Debug.LogWarning(string.Format("Failed to find method named {0}", fullyQualifiedTypeAndMethodName));
            return false;
        }

        private static bool InvokeMethod(string fullyQualifiedTypeAndMethodName, IEnumerable<string> nameSpaceParts, Type type)
        {
            string methodName = nameSpaceParts.Last();
            MethodInfo method = type.GetMethod(methodName);
            try
            {
                method.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(string.Format("Failed to invoke {0}. Exception thrown.", fullyQualifiedTypeAndMethodName));
                Debug.LogException(ex);
                return false;
            }
            return true;
        }

        private static Type GetTypeFromLoadedAssemblies(string typeName)
        {
            return (from asm in AppDomain.CurrentDomain.GetAssemblies()
                    let ype = asm.GetType(typeName)
                    where ype != null
                    select ype).SingleOrDefault();
        }

        private static void WriteResponseString(HttpListenerResponse response, Stream output, string responseString)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            output.Write(buffer, 0, buffer.Length);
        }

        private class AsyncCommand
        {
            public readonly AutoResetEvent CommandProcessed = new AutoResetEvent(false);

            public readonly List<LogEntry> LogMessages = new List<LogEntry>();
            public readonly string Method;
            public int ResultCode;

            public AsyncCommand(string method)
            {
                Method = method;
            }

            public string GetResultBody()
            {
                return "<!doctype html><html lang=\"en\"><body>" + string.Join("\n", LogMessages.Select(m => m.ToString()).ToArray()) + "</body></html>";
            }
        }

        private struct LogEntry
        {
            public string Condition;
            public LogType Type;

            public override string ToString()
            {
                return string.Format("{0}: {1}", Type, Condition);
            }
        }
    }
}
