using System.IO;
using Inspection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

public static class InspectionProjectSetup
{
    const string ScenePath = "Assets/Scenes/Inspection.unity";
    const string XRPath = "Assets/XR/Settings/InspectionXR.asset";

    [MenuItem("Inspection/Configure Android Project")]
    public static void Configure()
    {
        Directory.CreateDirectory("Assets/XR/Settings");
        AssetDatabase.Refresh();
        var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XRPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(settings, XRPath);
        }
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
        if (!settings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        settings.SettingsForBuildTarget(BuildTargetGroup.Android).InitManagerOnStart = true;
        var manager = settings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android))
            throw new BuildFailedException("Could not enable ARCore. Wait for Package Manager to finish, then configure again.");

        PlayerSettings.companyName = "InspectionDemo";
        PlayerSettings.productName = "AR Inspection";
        PlayerSettings.bundleVersion = "1.2.1";
        PlayerSettings.Android.bundleVersionCode = 4;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.inspection.prototype");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        IncludeRuntimeShaders();
        // This small project deliberately uses the legacy touch API and StandaloneInputModule.
        var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var input = playerSettings.FindProperty("activeInputHandler");
        if (input != null) { input.intValue = 0; playerSettings.ApplyModifiedProperties(); }

        if (!File.Exists(ScenePath)) CreateScene();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(settings.SettingsForBuildTarget(BuildTargetGroup.Android));
        EditorUtility.SetDirty(manager);
        AssetDatabase.SaveAssets();
        Debug.Log("Android configured: ARCore, ARM64, IL2CPP, GLES3, HTTP. Switch build platform to Android and build.");
    }

    static void IncludeRuntimeShaders()
    {
        // Shader.Find alone does not keep a shader in a player build.
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
        // Remove the lit shader inserted by the earlier setup: the marker needs only flat color.
        var included = new System.Collections.Generic.List<Shader>();
        for (int i = 0; i < shaders.arraySize; i++)
        {
            var existing = shaders.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (existing != null && existing.name != "Standard") included.Add(existing);
        }
        shaders.ClearArray();
        foreach (var existing in included)
        {
            int index = shaders.arraySize;
            shaders.InsertArrayElementAtIndex(index);
            shaders.GetArrayElementAtIndex(index).objectReferenceValue = existing;
        }
        foreach (var name in new[] { "Unlit/Color", "Sprites/Default" })
        {
            var shader = Shader.Find(name);
            if (shader == null) throw new BuildFailedException("Missing shader: " + name);
            bool found = false;
            for (int i = 0; i < shaders.arraySize; i++)
                if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) found = true;
            if (found) continue;
            int index = shaders.arraySize;
            shaders.InsertArrayElementAtIndex(index);
            shaders.GetArrayElementAtIndex(index).objectReferenceValue = shader;
        }
        graphics.ApplyModifiedProperties();
    }

    static void CreateScene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Inspection App").AddComponent<InspectionBootstrap>();
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    [MenuItem("Inspection/Build Android APK")]
    public static void BuildAndroid()
    {
        InspectionStabilityChecks.Run();
        Configure();
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            throw new BuildFailedException("Install Android Build Support (SDK, NDK, OpenJDK) through Unity Hub.");
        Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { ScenePath }, locationPathName = "Builds/inspection.apk",
            target = BuildTarget.Android, options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Android build failed. See the Console for details.");
    }
}
