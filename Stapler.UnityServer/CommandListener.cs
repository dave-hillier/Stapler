using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Stapler.UnityServer
{
    public class Test
    {
        public static void Method()
        {
            Debug.Log("Test method");
        }
    }

    [InitializeOnLoad]
    class CommandListener
    {
        private static readonly HttpListener Listener = new HttpListener();
        static CommandListener()
        {
            StartServer();
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        private static void StartServer()
        {
            string prefix = string.Format("http://*:13711/{0}/", Base64Encode(Application.dataPath));
            Debug.Log(string.Format("Starting Listener: {0} for \"{1}\"", prefix, Application.dataPath));
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

        private static void HandleRequest(object c)
        {
            var ctx = c as HttpListenerContext;
            if (ctx == null) return;
            var response = ctx.Response;
            using (var output = response.OutputStream)
            {
                switch (ctx.Request.HttpMethod)
                {
                    case "GET":
                        const string responseString1 = "<html><body>Running</body></html>";
                        WriteResponseString(response, output, responseString1);
                        break;
                    case "POST":
                        var istream = ctx.Request.InputStream;
                        using (var read = new StreamReader(istream))
                        {
                            var fqmn = read.ReadToEnd();
                            var parts = fqmn.Split('.');
                            var typeName = string.Join(".", parts.Take(parts.Length - 1).ToArray());

                            Debug.Log("Finding " + typeName);

                            var type = (from asm in AppDomain.CurrentDomain.GetAssemblies()
                                       let ype = asm.GetType(typeName)
                                       where ype != null
                                       select ype).SingleOrDefault();
                            if (type != null)
                            {
                                var methodName = parts.Last();
                                Debug.Log("Invoking " + methodName);
                                var method = type.GetMethod(methodName);
                                method.Invoke(null, null);
                            }
                            
                        }
                        const string responseString = "<html><body>Acknowledge</body></html>";
                        WriteResponseString(response, output, responseString);
                        break;
                }
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
