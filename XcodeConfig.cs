using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

#if UNITY_IOS || UNITY_EDITOR
public class ProjectPostProcess
{
    /* 主要官方文档
     * 获取SettingKey(苹果) https://developer.apple.com/library/archive/documentation/DeveloperTools/Reference/XcodeBuildSettingRef/1-Build_Setting_Reference/build_setting_ref.html
     * 获取SettingKey(网友) https://pewpewthespells.com/blog/buildsettings.html#gcc_generate_debugging_symbols
     * UnityAPI使用文档     https://docs.unity3d.com/cn/2018.4/ScriptReference/iOS.Xcode.PBXProject.html
     */

    [PostProcessBuild] // [PostProcessBuild(1)]//括号里的数字是有多个PostProcessBuild时执行顺序，0是第一个执行的
    public static void OnPostProcessBuild(BuildTarget buildTarget, string buildProjectPath)
    {
        if (buildTarget == BuildTarget.iOS)
        {
            UnityEngine.Debug.Log("开始配置Xcode工程");

            // 修改项目设置 如签名模式(手动、自动) BundleID Bitcode Framework *tbd 自定义资源(如代码中使用到的图片资源等) 等，后面两个参数依次为 TeamId、BundleID
            ProjectSetting(buildProjectPath, "xxx", "com.xx.xxx");

            // 修改Info.Plist文件  如权限  version  build，后面两个参数依次为 version、build appName
            InfoPlistSetting(buildProjectPath, "1.0", "1.0", "SDK框架");

            // 替换原生代码文件
            string[] unityFilePaths = { "Editor/AppleNative/UnityAppController.mm" };
            string[] xcodeFilePaths = { "Classes/UnityAppController.mm" };
            ReplaceFile(buildProjectPath, unityFilePaths, xcodeFilePaths);

            // 替换iOS中的某一行代码
            XEditCodeModel[] editCodeModes = { XEditCodeModel.ReplaceInit("Classes/UnityAppController.mm", "::printf(\"-> applicationDidFinishLaunching()\\n\");", "    ::printf(\"-> applicationDidFinishLaunching\\n\");") };
            ReplaceCode(buildProjectPath, editCodeModes);

            // 在iOS指定代码后面新增代码或方法
            XEditCodeModel[] insertCodeModes = { XEditCodeModel.InsertInit("Classes/UnityAppController.mm", "UnitySendDeviceToken(deviceToken);", "\n    NSLog(@\"这是Unity通过XcodeAPI插入的一行代码\");"),
            XEditCodeModel.InsertInit("Classes/UnityAppController.mm", "- (void)application:(UIApplication*)application didRegisterForRemoteNotificationsWithDeviceToken:(NSData*)deviceToken\n{\n    AppController_SendNotificationWithArg(kUnityDidRegisterForRemoteNotificationsWithDeviceToken, deviceToken);\n    UnitySendDeviceToken(deviceToken);\n\n    NSLog(@\"这是Unity通过XcodeAPI插入的一行代码\");\n\n}", "\n- (void)testUnitAddCustomMethod {\n    NSLog(@\"这是Unity通过XcodeAPI插入的一个方法\");\n}")
            };
            InsertCode(buildProjectPath, insertCodeModes);
        }
    }

    private static void ProjectSetting(string buildProjectPath, string teamId, string bundleID)
    {
        // 创建工程设置对象
        string projectPath = buildProjectPath + "/Unity-iPhone.xcodeproj/project.pbxproj";
        PBXProject project = new PBXProject();
        project.ReadFromFile(projectPath);

        // 选择要设置的target(获取指定的Target如：project.TargetGuidByName("Unity-iPhone")或者：project.TargetGuidByName("UnityFramework"))
        string mainTarget = project.GetUnityMainTargetGuid();
        string frameworkTarget = project.GetUnityFrameworkTargetGuid();
        // string testTarget = project.TargetGuidByName(PBXProject.GetUnityTestTargetName());

        // 证书签名模式 手动：Manual，自动：Automatic
        project.SetBuildProperty(mainTarget, "CODE_SIGN_STYLE", "Automatic");

        /* team ID(开发者中心会员信息处查看或者钥匙串里面的组织单位处查看)
         * 另一种设置方式：project.SetBuildProperty(mainTarget, "DEVELOPMENT_TEAM", teamId); 
         */
        project.SetTeamId(mainTarget, teamId);

        // p12证书的code_sign,既在钥匙串那里看双击钥匙串里已安装的证书最上面显示的标题就code_sign,也叫“常用名称
        project.SetBuildProperty(mainTarget, "CODE_SIGN_IDENTITY", "Apple Development");

        // 设置mobileprovison文件的Name(用vim打开.mobileprovision文件然后查找Name：在vim中输入/UUID，然后按回车键)
        // project.SetBuildProperty(mainTarget, "PROVISIONING_PROFILE_SPECIFIER", "mobileprovison文件的Name");
        // 设置mobileprovison文件的UUID(用vim打开.mobileprovision文件然后查找UUID：在vim中输入/UUID，然后按回车键)
        // project.SetBuildProperty(mainTarget, "PROVISIONING_PROFILE", "mobileprovison文件的UUID");

        // BundleID
        project.SetBuildProperty(mainTarget, "PRODUCT_BUNDLE_IDENTIFIER", bundleID);

        // 兼容版本
        project.SetBuildProperty(mainTarget, "IPHONEOS_DEPLOYMENT_TARGET", "12.0");

        /* 设置 BitCode
         * 如果需要对Debug、Release、ReleaseForProfiling、ReleaseForRunning分别设置，使用BuildConfigByName和SetBuildPropertyForConfig
         * 例如：string debugConfig = project.BuildConfigByName(mainTarget, "Debug");
         *      project.SetBuildPropertyForConfig(debugConfig, "ENABLE_BITCODE", "NO");
         */
        project.SetBuildProperty(frameworkTarget, "ENABLE_BITCODE", "NO");

        // Enable Objective-C Exceptions
        project.SetBuildProperty(frameworkTarget, "GCC_ENABLE_OBJC_EXCEPTIONS", "YES");

        // 设置 other link flags -ObjC
        // project.AddBuildProperty(mainTarget, "OTHER_LDFLAGS", "-ObjC");
        // project.SetBuildProperty(mainTarget, "OTHER_LDFLAGS", "-ObjC -all_load -lstdc++ -lsqlite3");

        /*
         * 添加SwiftPackageManager中的Framework
         * 当工程中用到了SwiftPakcageManager，则需要调用以下方法，其中AddRemotePackageFrameworkToProject既是
           添加到LinkBinaryWithLibraries中，framework的名字不需要带任何后缀
         * 由于remoteFramework全部是添加至frameworkTarget中，mainTarget中的代码可能会报错，如果mainTarget中也添加一次，
           会报两个target中有重复代码的提示，(如果报错的话)解决方法是把UnityFramework添加至Unity-iPhone的LinkBinaryWithLibraries中，代码如下：
         */
        string kingfisherGuid = project.AddRemotePackageReferenceAtVersion("https://github.com/onevcat/Kingfisher.git", "7.11.0");
        project.AddRemotePackageFrameworkToProject(frameworkTarget, "Kingfisher", kingfisherGuid, false);
        // 如果报错两个target中有重复代码，则执行如下代码
        //string file = "UnityFramework.framework";
        //string fileGuid = project.AddFile(file, file, PBXSourceTree.Build);
        //if (fileGuid != null)
        //{
        //    var sourcesBuildPhase = project.GetFrameworksBuildPhaseByTarget(mainTarget);
        //    project.AddFileToBuildSection(mainTarget, sourcesBuildPhase, fileGuid);
        //}

        // 添加系统Framework
        project.AddFrameworkToProject(frameworkTarget, "StoreKit.framework", true); // 内购需要 否则PBXCapabilityType.InAppPurchase会加不上

        /* 添加 .a、.tbd 文件
         * 两种方案
         * 方案一：project.AddFrameworkToProject(frameworkTarget, "libz.tbd", true);
         * 方案二：AddLibToProject(project, frameworkTarget, "libresolv.tbd");
         */
        project.AddFrameworkToProject(frameworkTarget, "libz.tbd", true);

        // 添加自定义动态库(Embed & Sign)
        // string defaultLocationInProject = Application.dataPath + "/Editor/Plugins/iOS"; // framework 存放的路径
        // const string coreFrameworkName = "xxxxx.framework"; // framework 的文件名
        // string framework = Path.Combine(defaultLocationInProject, coreFrameworkName);
        // string fileGuid = project.AddFile(framework, "Frameworks/" + coreFrameworkName, PBXSourceTree.Sdk);
        // PBXProjectExtensions.AddFileToEmbedFrameworks(project, frameworkTarget, fileGuid);
        // project.SetBuildProperty(frameworkTarget, "LD_RUNPATH_SEARCH_PATHS", "$(inherited) @executable_path/Frameworks");

        // 添加内购
        project.AddCapability(mainTarget, PBXCapabilityType.InAppPurchase);

        // 添加推送(需要把正确运行的Xcode工程的entitlements文件复制到Unity工程里一份(注意路径)，然后代码会自动复制进iOS工程里使用)
        // string entitlement = Application.dataPath + "/Editor/AppleNative/Unity-iPhone.entitlements";
        // File.Copy(entitlement, buildProjectPath + "/Unity-iPhone.entitlements");
        // project.AddCapability(mainTarget, PBXCapabilityType.PushNotifications, "Unity-iPhone.entitlements", true);

        // 创建一个空文件夹
        addFolder(project, mainTarget, buildProjectPath, "ImageFolder");

        // 添加图片、Bundle、文件夹等资源文件 资源放在 Assets/Editor/AppleNative 中
        AddAssets(project, mainTarget, "unityImageAdd.png", buildProjectPath + "/ImageFolder/", "/Editor/AppleNative/", false);
        AddAssets(project, mainTarget, "unityImageAdd0.png", buildProjectPath + "/", "/Editor/AppleNative/", true);
        AddAssets(project, mainTarget, "UnityImages1.bundle", buildProjectPath + "/", "/Editor/AppleNative/", true);
        AddAssets(project, mainTarget, "AssetsFile", buildProjectPath + "/", "/Editor/AppleNative/", true);


        // 修改后的内容写回到配置文件
        File.WriteAllText(projectPath, project.WriteToString());
    }


    // 添加lib
    private static void AddLibToProject(PBXProject project, string target, string lib)
    {
        string file = project.AddFile("usr/lib/" + lib, "Frameworks/" + lib, PBXSourceTree.Sdk);
        project.AddFileToBuild(target, file);
    }

    // 添加图片、Bundle、文件夹等资源文件  addBuild:是否要引用到Xcode中，可以解决调用addFolder方法后重复添加引用的问题， 默认True
    private static void AddAssets(PBXProject project, string target, string assetsName, string savePath, string unityAssetsPath, bool addBuild = true)
    {
        string assetsPath = Application.dataPath + unityAssetsPath + assetsName;
        RemoveDSStoreAndMetaFiles(assetsPath);

        try
        {
            RemoveFileReadOnlyAttribute(assetsPath);
            File.Copy(assetsPath, savePath + assetsName, true);
        }
        catch
        {
            Directory.CreateDirectory(savePath + assetsName);
            DirectoryCopy(assetsPath, savePath + assetsName);
        }

        if (addBuild)
        {
            // 将创建好的文件夹关联到Xcode工程中
            string file = project.AddFile(savePath + assetsName, assetsName, PBXSourceTree.Source);
            project.AddFileToBuild(target, file);
        }
    }

        // 在Xcode中创建一个文件夹
        private static void addFolder(PBXProject project, string target, string folderPath, string folderName)
    {
        string path = Path.Combine(folderPath, folderName);
        // 判断文件夹是否存在
        if (!Directory.Exists(path))
        {
            // 创建文件夹
            Directory.CreateDirectory(path);
        }

        // 将文件夹关联到Xcode工程中
        string file = project.AddFile(path, folderName, PBXSourceTree.Source);
        project.AddFileToBuild(target, file);
    }


    // 修改Info.Plist
    private static void InfoPlistSetting(string buildProjectPath, string version, string build, string appName)
    {
        string plistPath = Path.Combine(buildProjectPath, "Info.plist"); // 等价于：buildProjectPath + "/Info.plist";
        PlistDocument plist = new PlistDocument();
        plist.ReadFromFile(plistPath); // 等价于：plist.ReadFromString(File.ReadAllText(plistPath));

        // Get root
        PlistElementDict rootDict = plist.root;

        // 版本
        rootDict.SetString("CFBundleShortVersionString", version); // version
        rootDict.SetString("CFBundleVersion", build); // build

        // 权限
        // rootDict.SetString("NSPhotoLibraryUsageDescription", "为了能选择照片进行上传,请允许App访问您的相册"); // 相册

        // 允许HTTP请求 不设置的话 只能全部HTTPS请求
        var atsKey = "NSAppTransportSecurity";
        PlistElementDict dictTmp = rootDict.CreateDict(atsKey);
        dictTmp.SetBoolean("NSAllowsArbitraryLoads", true);

        // 设置App的名字
        rootDict.SetString("CFBundleDisplayName", appName);

        // 设置默认中文
        rootDict.SetString("CFBundleDevelopmentRegion", "zh_CN");

        // 设置BackgroundMode 远程推送
        // PlistElementArray bmArray = null;
        // if (!rootDict.values.ContainsKey("UIBackgroundModes"))
        // bmArray = rootDict.CreateArray("UIBackgroundModes");
        // else
        // bmArray = rootDict.values["UIBackgroundModes"].AsArray();
        // bmArray.values.Clear();
        // bmArray.AddString("remote-notification");


        // 增加白名单 scheme 打开别的app需要  比如 分享到微信 需要添加 wechat、weixin、weixinULAPI
        // PlistElement array = null;
        // if (rootDict.values.ContainsKey("LSApplicationQueriesSchemes"))
        // {
        // array = rootDict["LSApplicationQueriesSchemes"].AsArray();
        // }
        // else
        // {
        // array = rootDict.CreateArray("LSApplicationQueriesSchemes");
        // }
        // rootDict.values.TryGetValue("LSApplicationQueriesSchemes", out array);
        // PlistElementArray Qchemes = array.AsArray();
        // Qchemes.AddString("wechat");
        // Qchemes.AddString("weixin");
        // Qchemes.AddString("weixinULAPI");

        // 修改后的内容写回到文件Info.plist
        File.WriteAllText(plistPath, plist.WriteToString());
    }

    /* 替换原生代码文件
     * unityFilePaths：准备用来替换的放在Unity项目中的文件的路径数组
     * xcodeFilePaths：替换后的文件放在Xcode中的路径数组
     * 例如需要将放在Unity项目/Editor/AppleNative文件夹中的UnityAppController.mm文件替换到Xcode工程中的/Classes文件夹
       下，则unityFilePaths传入{ "Editor/AppleNative/UnityAppController.mm" }，xcodeFilePaths传入{ "Classes/UnityAppController.mm" }
     */
    private static void ReplaceFile(string buildProjectPath, string[] unityFilePaths, string[] xcodeFilePaths)
    {
        for (int i = 0; i < unityFilePaths.Length; i++)
        {
            string unityFilePath = unityFilePaths[i];
            string xcodeFilePath = xcodeFilePaths[i];

            string ReplaceFilePath = Application.dataPath + "/" + unityFilePath;
            string SaveFilePath = buildProjectPath + "/" + xcodeFilePath;
            if (File.Exists(SaveFilePath))
            {
                File.Delete(SaveFilePath);
            }
            File.Copy(ReplaceFilePath, SaveFilePath);
        }
    }

    /* 替换iOS中的某一行代码
     * editCodeModels 要替换的代码的信息，具体见XEditCodeModel定义
     * 例如需要将Xcode中Classes文件夹下UnityAppController.mm中的::printf("-> applicationDidFinishLaunching()\n");这一行
       代码替换为::printf("-> applicationDidFinishLaunching\n");，则editPath传入Classes/UnityAppController.mm，
       replaceCode传入::printf("-> applicationDidFinishLaunching()\n");，
       insertCode传入::printf("-> applicationDidFinishLaunching\n");
     */
    private static void ReplaceCode(string buildProjectPath, XEditCodeModel[] editCodeModels)
    {
        for (int i = 0; i < editCodeModels.Length; i++)
        {
            XEditCodeModel model = editCodeModels[i];

            // 找到要替换代码的文件
            XClass EditFile = new XClass(buildProjectPath + "/" + model.editPath);

            // 设置要替换的代码
            EditFile.ReplaceString(model.replaceCode, model.insertCode);
        } 
    }

    /* 在iOS指定代码后面新增代码或方法
     * editCodeModels 要新增的代码的信息，具体见XEditCodeModel定义
     * 新增代码：例如需要给Xcode中Classes文件夹下的UnityAppController.mm文件下的- (void)application:(UIApplication*)application didRegisterFor
                RemoteNotificationsWithDeviceToken:(NSData*)deviceToken 方法后面新增NSLog(@"这是Unity通过XcodeAPI插入的一行代码"); 方
                法，则editPath传入Classes/UnityAppController.mm， beforeCode传入UnitySendDeviceToken(deviceToken);，insertCode传
                入NSLog(@"这是Unity通过XcodeAPI插入的一行代码");
     * 新增方法：例如需要给Xcode中Classes文件夹下的UnityAppController.mm文件下的- (void)application:(UIApplication*)application didRegisterFor
                RemoteNotificationsWithDeviceToken:(NSData*)deviceToken 方法后面新增- (void)testUnitAddCustomMethod {\n    NSLog(@\"这是Unity通过XcodeAPI插入的一个方法\"); 方
                法，则editPath传入Classes/UnityAppController.mm， beforeCode传入- (void)application:(UIApplication*)application didRegisterForRemoteNotificationsWithDevice
                Token:(NSData*)deviceToken\n{\n    AppController_SendNotificationWithArg(kUnityDidRegisterForRemoteNotificationsWithDeviceToken, deviceToken);\n    Unity
                SendDeviceToken(deviceToken);\n\n    NSLog(@\"这是Unity通过XcodeAPI插入的一行代码\");，insertCode传
                入- (void)testUnitAddCustomMethod {\n    NSLog(@\"这是Unity通过XcodeAPI插入的一个方法\");
     */
    private static void InsertCode(string buildProjectPath, XEditCodeModel[] editCodeModels)
    {
        for (int i = 0; i < editCodeModels.Length; i++)
        {
            XEditCodeModel model = editCodeModels[i];

            // 找到要新增代码的文件
            XClass EditFile = new XClass(buildProjectPath + "/" + model.editPath);

            // 设置要新增的代码(参数一：要增加的位置前一行代码， 参数二：要增加的代码)
            EditFile.WriteBelowCode(model.beforeCode,  model.insertCode);
        }
    }

    // 定义文件更新类对象
    public partial class XEditCodeModel
    {
        /// 要修改的Xcode代码文件的路径
        public string editPath { get; set; }
        /// 要替换的Xcode的代码
        public string replaceCode { get; set; }
        /// 要增加Xcode的代码代码位置的前一行代码
        public string beforeCode { get; set; }
        /// 要新增的Xcode代码
        public string insertCode { get; set; }

        /// 便捷初始化方法(替换iOS中的某一行代码时专用)
        public static XEditCodeModel ReplaceInit(string editPath, string replaceCode, string insertCode)
        {
            XEditCodeModel model = new XEditCodeModel();
            model.editPath = editPath;
            model.replaceCode = replaceCode;
            model.insertCode = insertCode;

            return model;
        }

        /// 便捷初始化方法(在iOS指定代码后面新增一行代码时专用)
        public static XEditCodeModel InsertInit(string editPath, string beforeCode, string insertCode)
        {
            XEditCodeModel model = new XEditCodeModel();
            model.editPath = editPath;
            model.beforeCode = beforeCode;
            model.insertCode = insertCode;

            return model;
        }
    }


    // 定义文件更新类
    public partial class XClass : System.IDisposable
    {

        private string filePath;

        public XClass(string fPath) // 通过文件路径初始化对象
        {
            filePath = fPath;
            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError(filePath + "该文件不存在,请检查路径!");
                return;
            }
        }
        // 替换某些字符串
        public void ReplaceString(string oldStr, string newStr, string method = "")
        {
            if (!File.Exists(filePath))
            {

                return;
            }
            bool getMethod = false;
            string[] codes = File.ReadAllLines(filePath);
            for (int i = 0; i < codes.Length; i++)
            {
                string str = codes[i].ToString();
                if (string.IsNullOrEmpty(method))
                {
                    if (str.Contains(oldStr)) codes.SetValue(newStr, i);
                }
                else
                {
                    if (!getMethod)
                    {
                        getMethod = str.Contains(method);
                    }
                    if (!getMethod) continue;
                    if (str.Contains(oldStr))
                    {
                        codes.SetValue(newStr, i);
                        break;
                    }
                }
            }
            File.WriteAllLines(filePath, codes);
        }

        // 在某一行后面插入代码
        public void WriteBelowCode(string below, string text)
        {
            StreamReader streamReader = new StreamReader(filePath);
            string text_all = streamReader.ReadToEnd();
            streamReader.Close();

            int beginIndex = text_all.IndexOf(below);
            if (beginIndex == -1)
            {

                return;
            }

            int endIndex = text_all.LastIndexOf("\n", beginIndex + below.Length);

            text_all = text_all.Substring(0, endIndex) + "\n" + text + "\n" + text_all.Substring(endIndex);

            StreamWriter streamWriter = new StreamWriter(filePath);
            streamWriter.Write(text_all);
            streamWriter.Close();
        }

        public void Dispose()
        {

        }
    }

    // 去除文件只读属性
    private static void RemoveFileReadOnlyAttribute(string filePath)
    {
        FileInfo fileInfo = new FileInfo(filePath);
        // 检查文件是否存在
        if (fileInfo.Exists)
        {
            // 如果文件被标记为只读，则去除只读属性
            if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                File.SetAttributes(filePath, fileInfo.Attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    // 去除文件夹只读属性
    private static void RemoveFolderReadOnlyAttribute(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            var attributes = File.GetAttributes(folderPath);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(folderPath, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    // 文件夹拷贝
    public static void DirectoryCopy(string sourceDir, string targetDir)
    {
        RemoveFolderReadOnlyAttribute(sourceDir);

        DirectoryInfo dir = new DirectoryInfo(sourceDir);
        DirectoryInfo[] dirs = dir.GetDirectories();

        // 排除文件目录不存在的情况
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceDir}");
        }

        // 如果Xcode中目录不存在，就创建
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        RemoveDSStoreAndMetaFiles(sourceDir);

        // 获取并复制文件夹中的文件
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string tempPath = Path.Combine(targetDir, file.Name);

            if (!((file.Name == ".DS_Store") || (file.Name.Contains(".meta"))))
            {
                file.CopyTo(tempPath, true);
            }   
        }

        // 获取并复制文件夹中的子文件夹
        foreach (DirectoryInfo subdir in dirs)
        {
            string tempPath = Path.Combine(targetDir, subdir.Name);
            DirectoryCopy(subdir.FullName, tempPath);
        }
    }

    // 移除.meta .DS_Store 文件
    private static void RemoveDSStoreAndMetaFiles(string folderPath)
    {
        string assetsPath = Application.dataPath;
        string[] metaFiles = Directory.GetFiles(assetsPath, "*.meta", SearchOption.AllDirectories);
        string[] storeFiles = Directory.GetFiles(assetsPath, "*.DS_Store", SearchOption.AllDirectories);

        foreach (string metaFile in metaFiles)
        {
            File.Delete(metaFile);
        }

        foreach (string storeFile in storeFiles)
        {
            File.Delete(storeFile);
        }
    }
}
#endif