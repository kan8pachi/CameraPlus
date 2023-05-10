﻿using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using IPA.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using System.IO;
using System.Reflection;
using CameraPlus.HarmonyPatches;
using CameraPlus.Configuration;
using CameraPlus.Behaviours;
using CameraPlus.Utilities;
using CameraPlus.VMCProtocol;

namespace CameraPlus
{
    public class CameraPlusController : MonoBehaviour
    {
        public Action<Scene, Scene> ActiveSceneChanged;
        public static CameraPlusController Instance { get; private set; }
        public ConcurrentDictionary<string, GameObject> LoadedProfile = new ConcurrentDictionary<string, GameObject>();
        public ConcurrentDictionary<string, CameraPlusBehaviour> Cameras = new ConcurrentDictionary<string, CameraPlusBehaviour>();

        internal string currentProfile;
        internal bool MultiplayerSessionInit;
        internal bool existsVMCAvatar = false;
        internal bool isRestartingSong = false;
        internal Transform origin;

        internal Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
        private RenderTexture _renderTexture;
        private ScreenCameraBehaviour _screenCameraBehaviour;
        private CameraMoverPointer _cameraMovePointer;
        private bool _initialized = false;

        public UnityEvent OnFPFCToggleEvent = new UnityEvent();

        internal ExternalSender _externalSender = null;

        private WebCamTexture _webCamTexture = null;
        internal WebCamDevice[] webCamDevices;
        private WebCamCalibrator _webCamCal;

        private void Awake()
        {
            if (Instance != null)
            {
                Plugin.Log?.Warn($"Instance of {this.GetType().Name} already exists, destroying.");
                GameObject.DestroyImmediate(this);
                return;
            }
            GameObject.DontDestroyOnLoad(this);
            Instance = this;

            /* Remove old cfg comverter
            string path = Path.Combine(UnityGame.UserDataPath, $"{Plugin.Name}.ini");
            string backupPath = backupPath = Path.Combine(UnityGame.UserDataPath, Plugin.Name, "OldProfiles");
            if (File.Exists(path))
            {
                if (!Directory.Exists(backupPath))
                    Directory.CreateDirectory(backupPath);
                File.Copy(path, Path.Combine(backupPath, $"{Plugin.Name}.ini"), true);
                File.Delete(path);
            }
            
            ConfigConverter.ProfileConverter();
            */

            SceneManager.activeSceneChanged += this.OnActiveSceneChanged;
            CameraUtilities.CreateMainDirectory();
            CameraUtilities.CreateExampleScript();

            //ConfigConverter.DefaultConfigConverter();
        }
        private void Start()
        {
            if (PluginConfig.Instance.ScreenFillBlack)
            {
                _renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
                _screenCameraBehaviour = this.gameObject.AddComponent<ScreenCameraBehaviour>();
                _screenCameraBehaviour.SetCameraInfo(new Vector2(0, 0), new Vector2(Screen.width, Screen.height), -2000);
                _screenCameraBehaviour.SetRenderTexture(_renderTexture);
            }

            ShaderLoad();
            _cameraMovePointer = this.gameObject.AddComponent<CameraMoverPointer>();
            CameraUtilities.AddNewCamera(Plugin.MainCamera);
            MultiplayerSessionInit = false;

            _externalSender = new GameObject("ExternalSender").AddComponent<ExternalSender>();
            _externalSender.transform.SetParent(transform);

            if (CustomUtils.IsModInstalled("VMCAvatar","0.99.0"))
                existsVMCAvatar = true;
            _webCamTexture = new WebCamTexture();
            webCamDevices = WebCamTexture.devices;
        }

        private void ShaderLoad()
        {
            AssetBundle assetBundle = AssetBundle.LoadFromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("CameraPlus.Resources.Shader.customshader"));
            Shaders = assetBundle.LoadAllAssets<Shader>().ToDictionary(x => x.name);
        }

        private void OnDestroy()
        {
            MultiplayerSession.Close();
            SceneManager.activeSceneChanged -= this.OnActiveSceneChanged;
            Plugin.Log?.Debug($"{name}: OnDestroy()");
            Instance = null; // This MonoBehaviour is being destroyed, so set the static Instance property to null.
        }

        public void OnActiveSceneChanged(Scene from, Scene to)
        {
            if (isRestartingSong && to.name != "GameCore") return;
            if (_initialized || (!_initialized && (to.name == "HealthWarning" || to.name == "MainMenu")))
                SharedCoroutineStarter.instance.StartCoroutine(DelayedActiveSceneChanged(from, to));
#if DEBUG
            Plugin.Log.Info($"Scene Change {from.name} to {to.name}");
#endif
        }

        private IEnumerator DelayedActiveSceneChanged(Scene from, Scene to)
        {
            bool isRestart = isRestartingSong;
            bool isLevelEditor = false;
            _initialized = true;
            isRestartingSong = false;

            yield return waitMainCamera();

            if (!isRestart)
                CameraUtilities.ReloadCameras();

            IEnumerator waitForcam()
            {
                yield return new WaitForSeconds(0.1f);
                while (Camera.main == null) yield return new WaitForSeconds(0.05f);
            }

            if (PluginConfig.Instance.ProfileSceneChange && !isRestart)
            {
                if (!MultiplayerSession.ConnectedMultiplay || PluginConfig.Instance.MultiplayerProfile == "")
                {
                    if (to.name == "GameCore" && PluginConfig.Instance.SongSpecificScriptProfile != "" && CustomPreviewBeatmapLevelPatch.customLevelPath != "")
                        CameraUtilities.ProfileChange(PluginConfig.Instance.SongSpecificScriptProfile);
                    else if (to.name == "GameCore" && PluginConfig.Instance.RotateProfile != "" && LevelDataPatch.is360Level)
                        CameraUtilities.ProfileChange(PluginConfig.Instance.RotateProfile);
                    else if (to.name == "GameCore" && PluginConfig.Instance.GameProfile != "")
                        CameraUtilities.ProfileChange(PluginConfig.Instance.GameProfile);
                    else if ((to.name == "MainMenu" || to.name == "MenuCore" || to.name == "HealthWarning") && PluginConfig.Instance.MenuProfile != "")
                        CameraUtilities.ProfileChange(PluginConfig.Instance.MenuProfile);
                    else if (to.name == "BeatmapEditor3D" || to.name == "BeatmapLevelEditorWorldUi")
                    {
                        CameraUtilities.ClearCameras();
                        isLevelEditor = true;
                    }
                }
            }
            if (!isLevelEditor)
            {
                if (ActiveSceneChanged != null)
                {
                    yield return waitForcam();
                    // Invoke each activeSceneChanged event
                    foreach (var func in ActiveSceneChanged?.GetInvocationList())
                    {
                        try
                        {
                            func?.DynamicInvoke(from, to);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"Exception while invoking ActiveSceneChanged:" +
                                $" {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                }
                else if (PluginConfig.Instance.ProfileSceneChange && to.name == "HealthWarning" && PluginConfig.Instance.MenuProfile != "")
                    CameraUtilities.ProfileChange(PluginConfig.Instance.MenuProfile);

                yield return waitForcam();

                if (to.name == "GameCore")
                    origin = GameObject.Find("LocalPlayerGameCore/Origin")?.transform;
                if (to.name == "MainMenu")
                {
                    var chat = GameObject.Find("ChatDisplay");
                    if (chat)
                        chat.layer = Layers.UI;
                }
            }
        }

        internal IEnumerator waitMainCamera()
        {
            if (SceneManager.GetActiveScene().name == "GameCore")
            {
                while (!MainCameraPatch.isGameCameraEnable)
                    yield return null;
            }
            else
            {
                while (Camera.main == null)
                    yield return null;
            }
        }

        internal string[] WebCameraList()
        {
            string[] webcamera = new string[] { };
            List<string> list = new List<string>();
            if (_webCamTexture)
            {
                for (int i = 0; i < webCamDevices.Length; i++)
                    list.Add(webCamDevices[i].name);
                webcamera = list.ToArray();
            }
            return webcamera;
        }

        internal void WebCameraCalibration(CameraPlusBehaviour camplus)
        {
            _webCamCal = new GameObject("WebCamCalScreen").AddComponent<WebCamCalibrator>();
            _webCamCal.transform.SetParent(this.transform);
            _webCamCal.Init();
            _webCamCal.AddCalibrationScreen(camplus, Camera.main);
        }
        internal bool inProgressCalibration()
        {
            if (_webCamCal)
                return true;
            return false;
        }
        internal void DestroyCalScreen()
        {
            if (_webCamCal)
                Destroy(_webCamCal.gameObject);
        }
    }
}
