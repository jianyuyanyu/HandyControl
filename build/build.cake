﻿#addin nuget:?package=Cake.Git&version=4.0.0
#tool nuget:?package=NuGet.CommandLine&version=6.9.1

using System;
using System.Text.RegularExpressions;
using System.Linq;
using System.Net;
using System.Xml;
using System.Xml.Serialization;
using static System.IO.Directory;
using static System.IO.File;
using static System.IO.Path;

const string HandyControlBirthYear = "2018";
const string TagPrefix = "v";
const string RepositoryFolder = "..";
const string LibNuspecTemplateFilePath = "lib.nuspec.template";
const string LangNuspecTemplateFilePath = "lang.nuspec.template";
const string InstallerNuspecTemplateFilePath = "installer.nuspec.template";
const string ConfigFilePath = "build.config.xml";
const string NugetSourceUrl = "https://api.nuget.org/v3/index.json";

var target = Argument("target", "build");
var preReleasePhase = Argument("pre-release-phase", "rc");
var preRelease = Argument("pre-release", false);
var username = Argument("username", "NaBian");
var email = Argument("email", "836904362@qq.com");
var nugetToken = Argument("nuget-token", "");

var libVersion = "";
var nugetVersion = "";
var nugetFolder = "";
var installerFolder = "";
var year = $"{DateTime.Now.Year}";
var copyright = $"Copyright © HandyOrg {HandyControlBirthYear}-{year}";
var libNuspecFilePath = "";
var langNuspecFilePath = "";
var installerNuspecFilePath = "";
var squirrelExePath = "";
var gitRootPath = GitFindRootFromPath(MakeAbsolute(new DirectoryPath("."))).FullPath;
var propsFilePath = Combine(gitRootPath, "src/Directory.Build.Props");
var licenseFilePath = Combine(gitRootPath, "LICENSE");
var resxFileFolder = Combine(gitRootPath, "src/Shared/HandyControl_Shared/Properties/Langs");
var iconFilePath = Combine(gitRootPath, "src/Shared/HandyControlDemo_Shared/Resources/Img/icon.ico");
var buildConfig = new BuildConfig();

Setup(context =>
{
    buildConfig = LoadBuildConfig();

    nugetFolder = Combine(buildConfig.OutputsFolder, "nuget");
    installerFolder = Combine(buildConfig.OutputsFolder, "installer");

    libNuspecFilePath = Combine(nugetFolder, GetFileNameWithoutExtension(LibNuspecTemplateFilePath));
    langNuspecFilePath = Combine(nugetFolder, LangNuspecTemplateFilePath);
    installerNuspecFilePath = Combine(nugetFolder, InstallerNuspecTemplateFilePath);

    CleanDirectory(buildConfig.OutputsFolder);
    CreateDirectory(nugetFolder);
    CreateDirectory(installerFolder);

    context.Information($"preReleasePhase: {preReleasePhase}");
    context.Information($"preRelease: {preRelease}");

    var releaseVersion = GetNextVersion(GetLatestVersion(), preRelease, preReleasePhase);
    libVersion = releaseVersion.Split('-')[0] + ".0";
    nugetVersion = releaseVersion;

    context.Information($"libVersion: {libVersion}");
    context.Information($"nugetVersion: {nugetVersion}");

    Copy(LibNuspecTemplateFilePath, libNuspecFilePath, true);
    Copy(LangNuspecTemplateFilePath, langNuspecFilePath, true);
    Copy(InstallerNuspecTemplateFilePath, installerNuspecFilePath, true);

    DownloadSquirrelTools();
});

#region TASKS

Task("update license")
    .Does(() =>
{
    const int copyrightIndex = 2;

    string[] lines = ReadAllLines(licenseFilePath, Encoding.UTF8);
    lines[copyrightIndex] = copyright;
    WriteAllLines(licenseFilePath, lines);
});

Task("update version")
    .Does(() =>
{
    if (libVersion != "")
    {
        var document = LoadXmlDocument(propsFilePath);
        document.DocumentElement.SelectSingleNode("//PropertyGroup/Version").InnerText = libVersion;
        document.DocumentElement.SelectSingleNode("//PropertyGroup/FileVersion").InnerText = libVersion;
        document.DocumentElement.SelectSingleNode("//PropertyGroup/AssemblyVersion").InnerText = libVersion;
        SaveXmlDocument(document, propsFilePath);
    }

    if (nugetVersion != "")
    {
        ReplaceFileText(libNuspecFilePath, "version", nugetVersion);
        ReplaceFileText(langNuspecFilePath, "version", nugetVersion);
        ReplaceFileText(installerNuspecFilePath, "version", nugetVersion);
    }
});

Task("update copyright")
    .Does(() =>
{
    var document = LoadXmlDocument(propsFilePath);
    document.DocumentElement.SelectSingleNode("//PropertyGroup/Copyright").InnerText = copyright;
    SaveXmlDocument(document, propsFilePath);

    ReplaceFileText(libNuspecFilePath, "year", year);
    ReplaceFileText(langNuspecFilePath, "year", year);
    ReplaceFileText(installerNuspecFilePath, "year", year);
});

Task("commit files")
    .Does(() =>
{
    GitAddAll(gitRootPath);

    var exitCode = StartProcess(
        "git",
        new ProcessSettings {
            Arguments = "diff --cached --quiet",
            WorkingDirectory = gitRootPath
        }
    );
    
    if(exitCode == 0)
    {
        Information("no change.");
        return;
    }

    GitCommit(gitRootPath, username, email, $"chore: [AUTO] bump up version to {nugetVersion}.");
});

Task("create tag")
    .Does(() =>
{
    GitTag(gitRootPath, $"{TagPrefix}{nugetVersion}");
});


Task("generate change log")
    .Does(() =>
{
    // TODO
    WriteAllText(Combine(buildConfig.OutputsFolder, "CHANGELOG.md"), "");
});

Task("update nuget sha")
    .Does(() =>
{
    var lastCommit = GitLogTip(gitRootPath);

    ReplaceFileText(libNuspecFilePath, "commit", lastCommit.Sha);
    ReplaceFileText(langNuspecFilePath, "commit", lastCommit.Sha);
    ReplaceFileText(installerNuspecFilePath, "commit", lastCommit.Sha);
});

Task("add nuget dependencies")
    .Does(() =>
{
    AddDependenciesItem(libNuspecFilePath);
    AddDependenciesItem(langNuspecFilePath);

    void AddDependenciesItem(string xmlFilePath)
    {
        XmlDocument document = LoadXmlDocument(xmlFilePath);
        foreach (BuildTask task in buildConfig.BuildTasks)
        {
            var frameworkItem = document.CreateElement("group");
            frameworkItem.SetAttribute("targetFramework", task.Target);
            document.DocumentElement.SelectSingleNode("//metadata/dependencies").AppendChild(frameworkItem);
            SaveXmlDocument(document, xmlFilePath, indentation: 2);
        }
    }
});

Task("add nuget files")
    .Does(() =>
{
    XmlDocument libDocument = LoadXmlDocument(libNuspecFilePath);
    XmlDocument langDocument = LoadXmlDocument(langNuspecFilePath);

    foreach (BuildTask task in buildConfig.BuildTasks)
    {
        var libFolder = $@"lib\{task.OutputsFolder}";

        if (!IsFramework(task.Framework))
        {
            AddFile(libDocument, $@"{libFolder}\HandyControl.deps.json");
        }
        AddFile(libDocument, $@"{libFolder}\HandyControl.dll");
        AddFile(libDocument, $@"{libFolder}\HandyControl.pdb");
        AddFile(libDocument, $@"{libFolder}\HandyControl.xml");
        AddFile(langDocument, $@"{libFolder}\{{lang}}\HandyControl.resources.dll");
    }

    SaveXmlDocument(libDocument, libNuspecFilePath, indentation: 2);
    SaveXmlDocument(langDocument, langNuspecFilePath, indentation: 2);
});

Task("build lib")
    .Does(() =>
{
    foreach (var task in buildConfig.BuildTasks)
    {
        if (!task.BuildLib)
        {
            continue;
        }

        DotNetBuild
        (
            $"{gitRootPath}/src/{task.Domain}/HandyControl_{task.Domain}/HandyControl_{task.Domain}.csproj",
            new DotNetBuildSettings
            {
                Configuration = task.Configuration,
                Framework = task.Framework,
                OutputDirectory = $"{buildConfig.OutputsFolder}/lib/{task.OutputsFolder}",
            }
        );
    }
});

Task("build demo")
    .Does(() =>
{
    foreach (var task in buildConfig.BuildTasks)
    {
        if (!task.BuildDemo)
        {
            continue;
        }

        var dotNetBuildSettings = new DotNetBuildSettings
        {
            Configuration = task.Configuration,
            Framework = task.Framework,
            OutputDirectory = $"{buildConfig.OutputsFolder}/demo/{task.OutputsFolder}",
            // remove pdb files
            ArgumentCustomization = args => args
                .Append("/p:DebugType=none")
        };

        DotNetBuild
        (
            $"{gitRootPath}/src/{task.Domain}/HandyControlDemo_{task.Domain}/HandyControlDemo_{task.Domain}.csproj",
            dotNetBuildSettings
        );
    }
});

Task("create lang nuspec files")
    .Does(() =>
{
    string templateContent = ReadAllText(langNuspecFilePath, Encoding.UTF8);

    foreach (string lang in GetAllLangs())
    {
        string langFilePath = Combine(nugetFolder, $"lang.{lang}.nuspec");
        WriteAllText(langFilePath, templateContent.Replace("{lang}", lang));
    }
});

Task("create installer nuspec files")
    .Does(() =>
{
    string templateContent = ReadAllText(installerNuspecFilePath, Encoding.UTF8);

    foreach (var task in buildConfig.BuildTasks)
    {
        string installerFilePath = Combine(nugetFolder, $"installer.{task.Framework.Replace(".", "")}.nuspec");
        WriteAllText(installerFilePath, templateContent.Replace("{framework}", task.Framework.Replace(".", "")));

        XmlDocument installerDocument = LoadXmlDocument(installerFilePath);
        var demoFolder = $@"demo\{task.OutputsFolder}";
        // Squirrel is expecting a single lib / net45 directory provided regardless of whether your app is a net45 application.
        AddFile(installerDocument, $@"{demoFolder}\**\*", @"lib\net45");
        SaveXmlDocument(installerDocument, installerFilePath, indentation: 2);
    }
});

Task("pack nuspec files")
    .Does(() =>
{
    string nugetExePath = Context.Tools.Resolve("nuget.exe").FullPath;
    StartProcess(nugetExePath, $"pack {nugetFolder}/lib.nuspec -Symbols -SymbolPackageFormat snupkg -OutputDirectory {nugetFolder}");

    foreach (string nuspecFilePath in GetAllLangNuspecFilePaths())
    {
        StartProcess(nugetExePath, $"pack {nuspecFilePath} -OutputDirectory {nugetFolder}");
    }

    foreach (string nuspecFilePath in GetAllInstallerNuspecFilePaths())
    {
        StartProcess(nugetExePath, $"pack {nuspecFilePath} -OutputDirectory {nugetFolder}");
    }
});

Task("create demo installers")
    .Does(() =>
{
    if (!FileExists(squirrelExePath))
    {
        throw new Exception("Squirrel.exe not found.");
    }

    foreach (var task in buildConfig.BuildTasks)
    {
        Information(task.Framework);

        string releaseDir = Combine(installerFolder, task.OutputsFolder);
        CreateDirectory(releaseDir);

        string installerFilePath = Combine(nugetFolder, $"HandyControlDemo-{task.Framework.Replace(".", "")}.{nugetVersion}.nupkg");
        var cmd = $"--releasify {installerFilePath} --releaseDir {releaseDir} --setupIcon {iconFilePath} --icon {iconFilePath} --no-msi --no-delta";
        StartProcess(squirrelExePath, cmd);
    }
});

Task("push to nuget")
    .Does(() =>
{
    if (string.IsNullOrEmpty(nugetToken))
    {
        throw new Exception("NuGet token is required!");
    }

    var packages = GetFiles("../build/outputs/nuget/HandyControl.*nupkg");
    NuGetPush(packages, new NuGetPushSettings
    {
        ApiKey = nugetToken,
        Source = NugetSourceUrl,
        SkipDuplicate = true
    });
});

Task("publish")
    .IsDependentOn("update license")
    .IsDependentOn("update version")
    .IsDependentOn("update copyright")
    .IsDependentOn("commit files")
    .IsDependentOn("generate change log")
    .IsDependentOn("create tag")
    .IsDependentOn("update nuget sha")
    .IsDependentOn("add nuget dependencies")
    .IsDependentOn("add nuget files")
    .IsDependentOn("build lib")
    .IsDependentOn("build demo")
    .IsDependentOn("create lang nuspec files")
    .IsDependentOn("create installer nuspec files")
    .IsDependentOn("pack nuspec files")
    .IsDependentOn("create demo installers")
    .IsDependentOn("push to nuget")
    ;

Task("build")
    .IsDependentOn("build lib")
    .IsDependentOn("build demo")
    ;

RunTarget(target);

#endregion

#region HELP FUNCTIONS

private XmlDocument LoadXmlDocument(string xmlFilePath)
{
    var document = new XmlDocument();

    using (var reader = new XmlTextReader(xmlFilePath))
    {
        reader.Namespaces = false;
        document.Load(reader);
    }

    return document;
}

private void SaveXmlDocument(XmlDocument document, string xmlFilePath, int indentation = 4)
{
    using (var writer = new XmlTextWriter(xmlFilePath, Encoding.UTF8))
    {
        writer.Formatting = Formatting.Indented;
        writer.Indentation = indentation;
        writer.Namespaces = false;
        document.WriteContentTo(writer);
    }
}

public void AddFile(XmlDocument document, string filePath, string target = "")
{
    var fileItem = document.CreateElement("file");
    fileItem.SetAttribute("src", $@"..\{filePath}");
    fileItem.SetAttribute("target", target == "" ? filePath : target);
    document.DocumentElement.SelectSingleNode("//files").AppendChild(fileItem);
}

private void ReplaceFileText(string filePath, string key, string value)
{
    WriteAllText(
        filePath,
        ReadAllText(filePath, Encoding.UTF8).Replace($"{{{key}}}", value)
    );
}

private string GetLatestVersion()
{
    var lastTag = GitTags(gitRootPath, true).Last();
    Semver.TryParse(lastTag.FriendlyName.Replace(TagPrefix, ""), out var version);

    return version.ToString();
}

// author: https://github.com/orgs/HandyOrg/people/DingpingZhang
private static string GetNextVersion(string versionText, bool canBumpMinor, string previewVersion)
{
    if (string.IsNullOrEmpty(versionText))
    {
        return "0.1.0";
    }

    if (!Semver.TryParse(versionText, out var version))
    {
        throw new InvalidOperationException($"The {versionText} is not a valid version.");
    }

    bool isPreviewCurrent = !string.IsNullOrEmpty(version.PreviewCode);
    bool isPreviewNext = !string.IsNullOrEmpty(previewVersion);

    if (!isPreviewCurrent && !isPreviewNext)
    {
        // stable -> stable: bump(stable)
        if (canBumpMinor)
        {
            version.Minor++;
            version.Patch = 0;
        }
        else
        {
            version.Patch++;
        }
    }
    else if (!isPreviewCurrent && isPreviewNext)
    {
        // stable -> preview: bump(stable) + rc
        if (canBumpMinor)
        {
            version.Minor++;
            version.Patch = 0;
        }
        else
        {
            version.Patch++;
        }

        version.PreviewCode = previewVersion;
    }
    else if (isPreviewCurrent && !isPreviewNext)
    {
        // preview -> stable: preview - rc
        version.PreviewCode = string.Empty;
        version.PreviewPatch = 0;
    }
    else
    {
        // preview -> preview: stable + bump(rc)
        version.PreviewPatch++;
    }

    return version.ToString();
}

private bool IsFramework(string framework) => !framework.Contains(".");

private IEnumerable<string> GetAllLangs()
{
    foreach (string resxFilePath in EnumerateFiles(resxFileFolder, "Lang.*.resx"))
    {
        yield return GetFileNameWithoutExtension(resxFilePath.Replace("Lang.", ""));
    }
}

private IEnumerable<string> GetAllInstallerNuspecFilePaths() => EnumerateFiles(nugetFolder, "installer.*.nuspec");

private IEnumerable<string> GetAllLangNuspecFilePaths() => EnumerateFiles(nugetFolder, "lang.*.nuspec");

private BuildConfig LoadBuildConfig()
{
    var buildConfig = new BuildConfig();
    var configDocument = LoadXmlDocument(ConfigFilePath);

    var outputsItem = configDocument.DocumentElement.SelectSingleNode("//config/outputsFolder");
    buildConfig.OutputsFolder = outputsItem.InnerText;

    var tasksItem = configDocument.DocumentElement.SelectSingleNode("//config/tasks");
    foreach (XmlNode node in tasksItem.ChildNodes)
    {
        try
        {
            buildConfig.BuildTasks.Add(new BuildTask
            {
                Framework = node.Attributes["framework"].Value,
                Target = node.Attributes["target"].Value,
                OutputsFolder = node.Attributes["outputsFolder"].Value,
                Configuration = node.Attributes["configuration"].Value,
                Domain = node.Attributes["domain"].Value,
                BuildLib = bool.Parse(node.Attributes["buildLib"].Value),
                BuildDemo = bool.Parse(node.Attributes["buildDemo"].Value),
            });
        }
        catch
        {
            // ignored
        }
    }

    return buildConfig;
}

private void DownloadSquirrelTools()
{
    const string url = "https://www.nuget.org/api/v2/package/squirrel.windows/2.0.1";

    var nupkgFilePath = Combine("tools", "squirrel.windows.2.0.1.nupkg");
    var toolFolderPath = Combine("tools", "squirrel.windows.2.0.1");

    DownloadFile(url, nupkgFilePath);
    Unzip(nupkgFilePath, toolFolderPath, true);
    squirrelExePath = Combine(toolFolderPath, "tools", "Squirrel.exe");
}

#endregion

#region INNER TYPES

public class BuildConfig
{
    public string OutputsFolder { get; set; }

    public List<BuildTask> BuildTasks { get; set; } = new();
}

public class BuildTask
{
    public string Framework { get; set; }

    public string Target { get; set; }

    public string OutputsFolder { get; set; }

    public string Configuration { get; set; }

    public string Domain { get; set; }

    public bool BuildLib { get; set; }

    public bool BuildDemo { get; set; }
}

// author: https://github.com/orgs/HandyOrg/people/DingpingZhang
public class Semver
{
    private static readonly Regex SemverPattern = new Regex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(-(?<preCode>[a-z]+)(?<prePatch>\d+)?)?$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

    public int Major;
    public int Minor;
    public int Patch;
    public string PreviewCode;
    public int PreviewPatch;

    public override string ToString() => string.IsNullOrEmpty(PreviewCode)
        ? $"{Major}.{Minor}.{Patch}"
        : $"{Major}.{Minor}.{Patch}-{PreviewCode}{(PreviewPatch == 0 ? string.Empty : PreviewPatch)}";

    public static bool TryParse(string version, out Semver semver)
    {
        var match = SemverPattern.Match(version);

        semver = null;
        if (match.Success)
        {
            string prePatch = match.Groups["prePatch"].Value;
            semver = new Semver
            {
                Major = int.Parse(match.Groups["major"].Value),
                Minor = int.Parse(match.Groups["minor"].Value),
                Patch = int.Parse(match.Groups["patch"].Value),
                PreviewCode = match.Groups["preCode"].Value,
                PreviewPatch = string.IsNullOrEmpty(prePatch) ? 0 : int.Parse(prePatch),
            };
        }

        return match.Success;
    }

    public static int Compare(Semver left, Semver right)
    {
        var version1 = new Version(left.Major, left.Minor, left.Patch);
        var version2 = new Version(right.Major, right.Minor, right.Patch);

        int result = version1.CompareTo(version2);
        if (result != 0)
        {
            return result;
        }

        var isEmptyPreviewCode1 = string.IsNullOrEmpty(left.PreviewCode);
        var isEmptyPreviewCode2 = string.IsNullOrEmpty(right.PreviewCode);
        if (isEmptyPreviewCode1 && isEmptyPreviewCode2)
        {
            return 0;
        }

        if (isEmptyPreviewCode1)
        {
            return 1;
        }

        if (isEmptyPreviewCode2)
        {
            return -1;
        }

        result = string.Compare(left.PreviewCode, right.PreviewCode, ignoreCase: true);
        return result == 0 ? left.PreviewPatch - right.PreviewPatch : result;
    }
}

#endregion
