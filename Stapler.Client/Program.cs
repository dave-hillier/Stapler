using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Stapler.Client
{
    internal class Program
    {
        private const string QuitMethod = "Stapler.UnityServer.CommandListener.Quit";
        private const int MaxFailures = 30;

        private const string UrlFormat = "http://localhost:13711/{0}/";

        // TODO: UNITY_HOME environment variable
        private static readonly string UnityExecutable = Environment.GetEnvironmentVariable("UNITY_HOME") ?? @"C:\Program Files (x86)\Unity\Editor\Unity.exe";

        private static bool _batchMode;
        private static bool _quit;
        private static bool _nographics;

        private static string _projectPath;
        private static string _logFile;
        private static string _executeMethod;

        private static string LockFilePath
        {
            get { return Path.Combine(Path.Combine(_projectPath, "Temp"), "UnityLockfile"); }
        }

        private static string StaplerServiceUrl
        {
            get
            {
                string unityStylePath = _projectPath.Replace("\\", "/").TrimEnd('/');
                string encodedPath = Base64Encode(unityStylePath);
                return string.Format(UrlFormat, encodedPath);
            }
        }

        public static string Base64Encode(string plainText)
        {
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        private static void ParseOptions(string[] args)
        {
            _batchMode = args.Contains("-batchmode");
            _quit = args.Contains("-quit");
            _nographics = args.Contains("-nographics");
            _projectPath = args.SkipWhile(arg => arg != "-projectPath").Skip(1).FirstOrDefault();
            _logFile = args.SkipWhile(arg => arg != "-logFile").Skip(1).FirstOrDefault();
            _executeMethod = args.SkipWhile(arg => arg != "-executeMethod").Skip(1).FirstOrDefault();
        }

        private static IEnumerable<string> UnityOptions()
        {
            if (_batchMode)
                yield return "-batchmode";
            if (_nographics)
                yield return "-nographics";
            if (!string.IsNullOrEmpty(_projectPath))
                yield return "-projectPath \"" + _projectPath + "\"";
            if (!string.IsNullOrEmpty(_logFile))
                yield return "-logFile " + _logFile;
            // Executed method is posted to the running unity
        }

        private static void Main(string[] args)
        {
            ParseOptions(args);
            if (_executeMethod == null || _projectPath == null)
            {
                Console.WriteLine("executeMethod projectPath are both required.");
                return;
            }

            Task<bool> result = SendOrLaunchUnity();
            result.Wait();
            if (result.Result)
                Console.WriteLine("Success");
            else
                Console.Error.WriteLine("Failed");
        }

        private static async Task<bool> SendOrLaunchUnity()
        {
            if (!File.Exists(LockFilePath))
            {
                Console.WriteLine("UnityLockfile not found. Running Unity...");
                StartUnity();
            }
            bool result = await PostMethodToInvokeToServer(_executeMethod);

            if (_quit)
            {
                // TODO post a quit
                await PostMethodToInvokeToServer(QuitMethod);
            }

            return result;
        }

        private static void StartUnity()
        {
            EnsureServerDllExists();
            InvokeUnity();
            BlockUntilUnityStarted();
        }

        private static async void BlockUntilUnityStarted()
        {
            int errorCount = 0;
            bool successfulResponse = false;
            do
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        using (HttpResponseMessage response = await client.GetAsync(StaplerServiceUrl))
                        {
                            successfulResponse = response.IsSuccessStatusCode;
                        }
                    }
                }
                catch (Exception)
                {
                    if (++errorCount > MaxFailures)
                    {
                        throw new Exception("Too many errors waiting for Unity to start");
                    }
                }
                if (!successfulResponse)
                    await Task.Delay(TimeSpan.FromSeconds(1));
            } while (successfulResponse);
            await Task.Delay(TimeSpan.FromSeconds(1));
            Console.WriteLine("Got success from UnityServer");
        }

        private static void EnsureServerDllExists()
        {
            string editorFolder = Path.Combine(Path.Combine(_projectPath, "Assets"), "Editor");
            const string dll = "Stapler.UnityServer.dll";
            string destination = Path.Combine(editorFolder, dll);
            if (File.Exists(dll) && IsNewerThanDestination(dll, destination))
            {
                Directory.CreateDirectory(editorFolder);
                Console.WriteLine("Updating {0} at {1}...", dll, destination);
                File.Copy(dll, destination, true);
            }
            else
            {
                Console.WriteLine("Stapler.UnityServer.dll missing. Not copying to Assets\\Editor.");
            }
        }

        private static bool IsNewerThanDestination(string dll, string destFolder)
        {
            return File.GetLastWriteTimeUtc(dll) > File.GetLastWriteTimeUtc(Path.Combine(destFolder, dll));
        }

        private static async Task<bool> PostMethodToInvokeToServer(string executeMethod)
        {
            using (var client = new HttpClient())
            {
                try
                {
                    using (
                        HttpResponseMessage response =
                            await client.PostAsync(StaplerServiceUrl, new StringContent(executeMethod)))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            return await HandleResponse(response);
                        }
                        Console.WriteLine("Error. UnityServer returned: {0}", response.ReasonPhrase);
                        Console.WriteLine(
                            "Ensure that UnityServer is running and that there are no suprious UnityLockfiles in you project.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception attempting to contact Stapler Server: {0}", ex);
                }
            }
            return false;
        }

        private static async Task<bool> HandleResponse(HttpResponseMessage response)
        {
            using (HttpContent content = response.Content)
            {
                string result = await content.ReadAsStringAsync();
                if (result != null)
                {
                    Console.WriteLine(result);
                    return true;
                }
            }
            return false;
        }

        private static void InvokeUnity()
        {
            string args = string.Join(" ", UnityOptions().ToArray());
            Console.WriteLine("{0} {1}", UnityExecutable, args);
            Process.Start(UnityExecutable, args);
        }
    }
}