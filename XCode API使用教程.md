1、下载XcodeAPI 库 

2、从路径XcodeAPIWithUnity->Assets->Editor->XCodeAPI->Scripts 文件夹内找到Xcode文件夹

3、将找到的Xcode文件夹放进工程Assets目录下某个Editor文件夹下(若没有Editor文件夹就自己在Assets目录下创建一个Editor文件夹)

4、将XcodeConfig文件放入Editor文件夹内

5、如XcodeConfig文件内报错找不到 using UnityEditor.iOS.Xcode.Custom，则将Unity.iOS.Extensions.Xcode文件放进步骤3的Xcode文件夹内(本文件夹内及Github连接下载的XcodeAPI文件已默认放入了Unity.iOS.Extensions.Xcode文件)

6、愉快的在XcodeConfig中进行自定义设置吧

获取设置使用的Key：(推荐)终端内cd到Xcode工程所在目录，然后输入命令：xcodebuild -showBuildSettings 

​                                      或者：https://developer.apple.com/library/archive/documentation/DeveloperTools/Reference/XcodeBuildSettingRef/1-Build_Setting_Reference/build_setting_ref.html

​                                     或者：https://pewpewthespells.com/blog/buildsettings.html#gcc_generate_debugging_symbols

​                                     或者：参考文件：SettingKey(Apple)和SettingKey(Other)

Unity官方文档：https://docs.unity3d.com/cn/2018.4/ScriptReference/iOS.Xcode.PBXProject.html

