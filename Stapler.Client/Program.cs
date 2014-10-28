using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;

namespace Stapler.Client
{
    class Program
    {
        private static bool _batchMode;
        private static bool _quit;
        private static bool _nographics;

        private static string _projectPath;
        private static string _logFile;
        private static string _executeMethod;

        private const string UrlFormat = "http://localhost:13711/{0}/";
        private const string UnityExecutable = @"C:\Program Files (x86)\Unity\Editor\Unity.exe";

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        static void ParseOptions(string[] args)
        {
            _batchMode = args.Contains("-batchmode");
            _quit = args.Contains("-quit");
            _nographics = args.Contains("-nographics");
            _projectPath = args.SkipWhile(arg => arg != "-projectPath").Skip(1).FirstOrDefault();
            _logFile = args.SkipWhile(arg => arg != "-logFile").Skip(1).FirstOrDefault();
            _executeMethod = args.SkipWhile(arg => arg != "-executeMethod").Skip(1).FirstOrDefault();
        }

        static IEnumerable<string> UnityOptions()
        {
            if (_batchMode)
                yield return "-batchmode";
            if (_quit)
                yield return "-quit";
            if (_nographics)
                yield return "-nographics";
            if (!string.IsNullOrEmpty(_projectPath))
                yield return "-projectPath \"" + _projectPath + "\"";
            if (!string.IsNullOrEmpty(_executeMethod))
                yield return "-executeMethod " + _executeMethod;
            if (!string.IsNullOrEmpty(_logFile))
                yield return "-logFile " + _logFile;
        }
        static void Main(string[] args)
        {
            ParseOptions(args);
            if (_executeMethod == null || _projectPath == null)
            {
                Console.WriteLine("executeMethod projectPath are both required.");
                return;
            }
            var t = new Task(SendOrLaunchUnity);
            t.Start();
            Console.WriteLine("Enter to exit...");
            Console.ReadLine();
        }

        static async void SendOrLaunchUnity()
        {
            var lockFilePath = Path.Combine(Path.Combine(_projectPath, "Temp"), "UnityLockfile");
            if (!File.Exists(lockFilePath))
            {
                EnsureServerDllExists();
                InvokeUnity();
            }
            else
            {
                await PostMethodToInvokeToServer();
            }
        }

        private static void EnsureServerDllExists()
        {
            const string dll = "Stapler.UnityServer.dll";
            if (File.Exists(dll))
            {
                var editorFolder = Path.Combine(Path.Combine(_projectPath, "Assets"), "Editor");
                File.Copy(dll, editorFolder, true);
            }
            else
            {
                Console.WriteLine("Stapler.UnityServer.dll missing. Not copying to Assets\\Editor.");
            }
        }

        private static async Task PostMethodToInvokeToServer()
        {
            var unityStylePath = _projectPath.Replace("\\", "/").TrimEnd('/');
            var encodedPath = Base64Encode(unityStylePath);
            var url = string.Format(UrlFormat, encodedPath);

            using (var client = new HttpClient())
            {
                try
                {
                    using (var response = await client.PostAsync(url, new StringContent(_executeMethod)))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            await HandleResponse(response);
                        }
                        else
                        {
                            Console.WriteLine("Error attempting to contact Stapler Server: {0}", response.StatusCode);
                            InvokeUnity();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception attempting to contact Stapler Server: {0}", ex);
                }
            }
        }

        private static async Task HandleResponse(HttpResponseMessage response)
        {
            using (var content = response.Content)
            {
                string result = await content.ReadAsStringAsync();
                if (result != null)
                {
                    Console.WriteLine(result);
                }
            }
        }

        private static void InvokeUnity()
        {
            // TODO: ensure that the server dll is in the Unity Editor folder
            var args = string.Join(" ", UnityOptions().ToArray());
            Console.WriteLine("{0} {1}", UnityExecutable, args);
            Process.Start(UnityExecutable, args);
        }
    }


}
