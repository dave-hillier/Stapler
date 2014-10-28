Thin client and server for [Unity](http://unity3d.com) inspired by [Nailgun](http://www.martiansoftware.com/nailgun/)

Stapler is designed to solve the problem that you cannot run a command line build while the editor has the project running.

It does this by providing a server and client. The server is a Unity Editor plugin that will invoke a method specified in a HTTP post. 
The client will check to see if the project is opened. If it is, it will make the HTTP call, otherwise it will start a new instance of Unity with the specified parameters. 

#Status

This project is still at concept stage; it is untested and has not been used outside of basic tests.

#Usage

On first usage with a project ensure that Unity is not running so that Stapler can copy the required plugin.

For a description of the above arguments see [Unity Command Line Arguments](http://docs.unity3d.com/Manual/CommandLineArguments.html)

**Required arguments:**
```
-projectPath <path to project>
-executeMethod <Class.StaticMethodToInvoke>
```
**Optional:**

Note: the following arguments are ignored if Unity is already running. 
```
-batchmode
-quit
-nographics
-logFile <log file name>
```

**Example:**

```
Stapler.Client -projectPath "C:\My Project" -executeMethod ClassName.MethodName
```

