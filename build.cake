#addin "nuget:?package=Cake.SemVer&version=1.0.14"
//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Get only solution file
var solutionFile = GetFiles("./*.sln").First();
// Define directories and files
var buildDir = Directory("./GlobalNews.Exportar/bin") + Directory(configuration);
var projFile = File("./GlobalNews.Exportar/GlobalNews.Exportar.csproj");
var assemblyInfoFile = File("./GlobalNews.Exportar/Properties/AssemblyInfo.cs");
var packageTarget = Directory("./net40");
var distDir = Directory("./dist");

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory(buildDir);
    CleanDirectory(distDir);
});

Task("Restore-NuGet-Packages")
    .IsDependentOn("Clean")
    .Does(() =>
{
    NuGetRestore(solutionFile);
});

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
    // Use MSBuild
    MSBuild(solutionFile, settings =>
    settings.SetConfiguration(configuration));
});

Task("Create-NuGet-Packages")
    .IsDependentOn("Build")
    .WithCriteria(() => Jenkins.IsRunningOnJenkins)
    .Does(() =>
{
    var assemblyInfo = ParseAssemblyInfo(assemblyInfoFile);
    var assemblyVersion = ParseSemVer(assemblyInfo.AssemblyVersion);
    //var packageVersion = assemblyVersion.Change(build: Jenkins.Environment.Build.BuildNumber);
    var packageVersion = assemblyVersion;

    NuGetPack(projFile, new NuGetPackSettings
    {
        OutputDirectory = distDir,
        Properties = new Dictionary<string, string> 
        {
            { "Configuration", configuration }
        },
        Version = packageVersion.ToString()
    });
});

Task("Prepare-NuGet-Packages")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => Jenkins.IsRunningOnJenkins)
    .Does(() =>
{
    var unzippedDirectory = distDir + Directory("./unzipped");
    var originalNugetPackage = GetFiles(distDir.ToString() + "/*.nupkg").First();
    var libDir = unzippedDirectory + Directory("./lib") + packageTarget;

    Unzip(originalNugetPackage, unzippedDirectory);
    
    if (DirectoryExists(unzippedDirectory + Directory("./content/build")))
    {
        MoveDirectory(unzippedDirectory + Directory("./content/build"), unzippedDirectory + Directory("./build"));
    }
    
    var libFiles = GetFiles(libDir.ToString() + "./*");
    foreach(var assemblyFile in libFiles)
    {
        var extension = assemblyFile.GetExtension();
        if (extension == ".exe" || extension == ".dll")
        {
            var pdbFile = buildDir + assemblyFile.GetFilename().ChangeExtension(".pdb");
            try
            {
                CopyFileToDirectory(pdbFile, libDir);
            }
            catch
            {
                Warning("The file {0} has no .pdb file to be copied", assemblyFile);
            }
        }
    }

    var contentTypesXml = unzippedDirectory + File("./[Content_Types].xml");
    Verbose("Adding .pdb to ContentTypes XML: {0}", contentTypesXml.ToString());
    var xmlDoc = System.Xml.Linq.XDocument.Load(contentTypesXml);
    System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
    var pdbContentType = new System.Xml.Linq.XElement(ns + "Default");
    pdbContentType.Add(new System.Xml.Linq.XAttribute("Extension", "pdb"));
    pdbContentType.Add(new System.Xml.Linq.XAttribute("ContentType", "application/octet"));
    xmlDoc.Element(ns + "Types").Add(pdbContentType);
    xmlDoc.Save(contentTypesXml);
    Verbose("Successfully added .pdb to ContentTypes XML: {0}", contentTypesXml.ToString());

    Zip(unzippedDirectory, originalNugetPackage);
    DeleteDirectory(unzippedDirectory, recursive:true);
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Prepare-NuGet-Packages");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
