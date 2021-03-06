﻿// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.VisualStudio.SlnGen
{
    internal static class MSBuildLocator
    {
        private static readonly Regex NetCoreBasePathRegex = new Regex(@"^\s+Base Path:\s+(?<Path>.*)$");

        public static bool TryLocate(Action<string> error, out VisualStudioInstance instance, out string msbuildBinPath)
        {
            instance = null;
            msbuildBinPath = null;

            string msbuildToolset = Environment.GetEnvironmentVariable("MSBuildToolset")?.Trim();

            if (!msbuildToolset.IsNullOrWhiteSpace())
            {
                string msbuildToolsPath = Environment.GetEnvironmentVariable($"MSBuildToolsPath_{msbuildToolset}")?.Trim();

                if (!msbuildToolsPath.IsNullOrWhiteSpace())
                {
                    if (Program.IsNetCore)
                    {
                        error("The .NET Core version of SlnGen is not supported in CoreXT.  You must use the .NET Framework version via the SlnGen.Corext package");

                        return false;
                    }

                    msbuildBinPath = msbuildToolsPath;

                    if (Version.TryParse(Environment.GetEnvironmentVariable("VisualStudioVersion") ?? string.Empty, out Version visualStudioVersion))
                    {
                        if (visualStudioVersion.Major <= 14)
                        {
                            error("MSBuild.Corext version 15.0 or greater is required");

                            return false;
                        }

                        VisualStudioConfiguration configuration = new VisualStudioConfiguration();

                        instance = configuration.GetLaunchableInstances()
                            .Where(i => !i.IsBuildTools && i.HasMSBuild && i.InstallationVersion.Major == visualStudioVersion.Major)
                            .OrderByDescending(i => i.InstallationVersion)
                            .FirstOrDefault();
                    }
                    else
                    {
                        error("The VisualStudioVersion environment variable must be set in CoreXT");

                        return false;
                    }

                    Program.IsCorext = true;

                    return true;
                }
            }

            TryGetVisualStudioFromDeveloperConsole(out instance);

            if (Program.IsNetCore)
            {
                if (!TryGetMSBuildInNetCore(out msbuildBinPath, out string errorMessage))
                {
                    if (!string.IsNullOrWhiteSpace(errorMessage))
                    {
                        error($"Failed to find .NET Core: {errorMessage}");
                    }
                    else
                    {
                        error("Failed to find .NET Core.  Run dotnet --info for more information.");
                    }

                    return false;
                }

                return true;
            }

            if (instance == null)
            {
                error("You must run SlnGen in a Visual Studio Developer Command Prompt");

                return false;
            }

            msbuildBinPath = Path.Combine(
                instance.InstallationPath,
                "MSBuild",
                instance.InstallationVersion.Major >= 16 ? "Current" : "15.0",
                "Bin");

            return true;
        }

        private static bool TryGetMSBuildInNetCore(out string msbuildPath, out string error)
        {
            msbuildPath = null;

            error = null;

            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    Arguments = "--info",
                    CreateNoWindow = true,
                    FileName = "dotnet",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };

            // https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet#environment-variables
            process.StartInfo.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            process.StartInfo.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE "] = "en-US";
            process.StartInfo.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP "] = "0";
            process.StartInfo.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
            process.StartInfo.EnvironmentVariables["COREHOST_TRACE"] = "0";

            try
            {
                if (!process.Start())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if (!process.WaitForExit((int)TimeSpan.FromSeconds(2).TotalMilliseconds))
            {
                return false;
            }

            error = process.StandardError.ReadLine();

            if (!string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            string line;

            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                Match match = NetCoreBasePathRegex.Match(line);

                if (!match.Success || !match.Groups["Path"].Success)
                {
                    continue;
                }

                msbuildPath = match.Groups["Path"].Value.Trim();

                return true;
            }

            return false;
        }

        private static bool TryGetVisualStudioFromDeveloperConsole(out VisualStudioInstance instance)
        {
            instance = null;

            string vsInstallDirEnvVar = Environment.GetEnvironmentVariable("VSINSTALLDIR");

            if (vsInstallDirEnvVar.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (!Directory.Exists(vsInstallDirEnvVar))
            {
                return false;
            }

            VisualStudioConfiguration configuration = new VisualStudioConfiguration();

            instance = configuration.GetInstanceForPath(vsInstallDirEnvVar);

            return instance != null;
        }
    }
}