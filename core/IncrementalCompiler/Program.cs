﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NLog;
using NLog.Config;
using NLog.Targets;
using System.ServiceModel;

namespace IncrementalCompiler
{
    partial class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "-dev")
            {
                return RunAsDev(args);
            }
            else if (args.Length > 0 && args[0] == "-server")
            {
                return RunAsServer(args);
            }
            else
            {
                return RunAsClient(args);
            }
        }

        static int RunAsClient(string[] args)
        {
            SetupLogger("IncrementalCompiler.log");

            var logger = LogManager.GetLogger("Client");
            logger.Info("Started");

            var currentPath = Directory.GetCurrentDirectory();
            var options = new CompileOptions();
            options.ParseArgument(args, currentPath);
            options.References = options.References.Distinct().ToList();
            options.Files = options.Files.Distinct().ToList();

            logger.Info("CurrentDir: {0}", Directory.GetCurrentDirectory());
            logger.Info("Output: {0}", options.Output);

            if (string.IsNullOrEmpty(options.Output))
            {
                logger.Error("No output");
                return 1;
            }

            // Get unity process ID

            var parentProcessId = 0;
            var pd = options.Defines.FirstOrDefault(d => d.StartsWith("__UNITY_PROCESSID__"));
            if (pd != null)
            {
                int.TryParse(pd.Substring(19), out parentProcessId);
            }
            else
            {
                var parentProcess = Process.GetProcessesByName("Unity").FirstOrDefault();
                if (parentProcess != null)
                    parentProcessId = parentProcess.Id;
            }

            if (parentProcessId == 0)
            {
                logger.Error("No parent process");
                return 1;
            }

            logger.Info("Parent process ID: {0}", parentProcessId);

            // Run

            Process serverProcess = null;
            while (true)
            {
                try
                {
                    var w = new Stopwatch();
                    w.Start();
                    logger.Info("Request to server");
                    var result = CompilerServiceClient.Request(parentProcessId, currentPath, options);
                    w.Stop();
                    logger.Info("Done: Succeeded={0}. Duration={1}sec.", result.Succeeded, w.Elapsed.TotalSeconds);
                    foreach (var warning in result.Warnings)
                        logger.Info(warning);
                    foreach (var error in result.Errors)
                        logger.Info(error);
                    return result.Succeeded ? 0 : 1;
                }
                catch (EndpointNotFoundException)
                {
                    if (serverProcess == null)
                    {
                        logger.Info("Spawn server");
                        serverProcess = Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = Assembly.GetEntryAssembly().Location,
                                Arguments = "-server " + parentProcessId,
                                WindowStyle = ProcessWindowStyle.Hidden
                            });
                        Thread.Sleep(100);
                    }
                    else
                    {
                        if (serverProcess.HasExited == false)
                            Thread.Sleep(100);
                        else
                            return 1;
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e, "Error in request");
                    return 1;
                }
            }
        }

        static int RunAsServer(string[] args)
        {
            SetupLogger("IncrementalCompiler-Server.log");

            var logger = LogManager.GetLogger("Server");
            logger.Info("Started");

            var parentProcessId = 0;
            if (args.Length >= 2 && int.TryParse(args[1], out parentProcessId) == false)
            {
                logger.Error("Error in parsing parentProcessId (arg={0})", args[1]);
                return 1;
            }

            return CompilerServiceServer.Run(logger, parentProcessId);
        }

        static void SetupLogger(string fileName)
        {
            var config = new LoggingConfiguration();

            var consoleTarget = new ColoredConsoleTarget
            {
                Layout = @"${date}|${logger}|${message}|${exception:format=tostring}"
            };
            config.AddTarget("console", consoleTarget);
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, consoleTarget));

            var logDirectory = Directory.Exists(".\\Temp") ? ".\\Temp\\" : ".\\";
            var fileTarget = new FileTarget
            {
                FileName = logDirectory + fileName,
                Layout = @"${longdate} ${uppercase:${level}}|${logger}|${message}|${exception:format=tostring}"
            };
            config.AddTarget("file", fileTarget);
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));

            LogManager.Configuration = config;
        }
    }
}
