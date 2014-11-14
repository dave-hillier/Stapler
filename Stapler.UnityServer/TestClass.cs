using System.Threading;
using UnityEngine;

namespace Stapler.UnityServer
{
    public class TestClass
    {
        public static void Method()
        {
            Debug.Log("Test method");
        }

        public static void LongRunning()
        {
            for (int i = 0; i < 30; ++i)
            {
                Debug.Log("Tick: " + i);
                Thread.Sleep(1000);
            }
        }
    }
}