using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ObraDinnInGameHints
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    public sealed class InGameHintsPlugin : BaseUnityPlugin
    {
        internal const string PluginId = "local.obradinn.ingamehints";
        internal const string PluginName = "Obra Dinn In-Game Hints";
        internal const string PluginVersion = "1.0.5";

        internal static InGameHintsPlugin Instance;
        private static bool mouseInputOverrideActive;

        internal static bool MouseInputOverrideActive
        {
            get { return mouseInputOverrideActive; }
        }

        private Harmony harmony;
        private EmbeddedHintRepository repository;
        private GameProgressSource progressSource;
        private JsonHintStateStore stateStore;
        private HintEngine engine;
        private DuckUiController ui;
        private GameObject uiHost;

        private ConfigEntry<bool> enabledEntry;
        private ConfigEntry<bool> soundEntry;
        private ConfigEntry<KeyboardShortcut> shortcutEntry;

        internal bool HintsEnabled
        {
            get { return enabledEntry.Value; }
            set { enabledEntry.Value = value; }
        }

        internal bool SoundEnabled
        {
            get { return soundEntry.Value; }
            set { soundEntry.Value = value; }
        }

        internal KeyboardShortcut Shortcut
        {
            get { return shortcutEntry.Value; }
        }

        internal HintEngine Engine
        {
            get { return engine; }
        }

        internal void DiagLog(string message)
        {
            Diag(message);
        }

        internal void SetMouseInputOverride(bool active)
        {
            mouseInputOverrideActive = active;
        }

        internal GameProgressSource ProgressSource
        {
            get { return progressSource; }
        }

        internal IHintRepository Repository
        {
            get { return repository; }
        }

        // ---- lifecycle diagnostics (immediate-flush, independent of BepInEx buffered log) ----
        private static readonly object DiagLock = new object();
        private static string DiagPath;

        private static void Diag(string message)
        {
            try
            {
                if (DiagPath == null)
                    DiagPath = Path.Combine(Paths.ConfigPath, "ObraDinnHints.diag.log");
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\r\n";
                lock (DiagLock)
                {
                    using (FileStream fs = new FileStream(DiagPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        sw.Write(line);
                        sw.Flush();
                        fs.Flush();
                    }
                }
            }
            catch { }
        }

        private void DumpSelf(string phase)
        {
            try
            {
                Diag(phase
                    + " enabled=" + enabled
                    + " activeSelf=" + gameObject.activeSelf
                    + " activeInHierarchy=" + gameObject.activeInHierarchy
                    + " goName=" + gameObject.name
                    + " goId=" + gameObject.GetInstanceID()
                    + " compId=" + GetInstanceID()
                    + " goHideFlags=" + gameObject.hideFlags);
            }
            catch (Exception ex) { Diag(phase + " DumpSelf failed: " + ex.GetType().Name); }
        }

        private void Awake()
        {
            Instance = this;
            Diag("Plugin.Awake");
            DumpSelf("Awake");
            try
            {
                enabledEntry = Config.Bind("General", "Enabled", true, "Enable the in-game hint assistant.");
                soundEntry = Config.Bind("General", "Sound", true, "Play the book help sound when a hint is revealed.");
                shortcutEntry = Config.Bind(
                    "General",
                    "Shortcut",
                    new KeyboardShortcut(KeyCode.H, new KeyCode[0]),
                    "Keyboard shortcut while the book is open.");

                repository = new EmbeddedHintRepository();
                progressSource = new GameProgressSource(Diag);
                string statePath = Path.Combine(Path.Combine(Paths.ConfigPath, "ObraDinnInGameHints"), "state.json");
                stateStore = new JsonHintStateStore(statePath, LogWarning);
                engine = new HintEngine(repository, progressSource, stateStore);

                uiHost = new GameObject("ObraDinnInGameHints");
                DontDestroyOnLoad(uiHost);
                ui = uiHost.AddComponent<DuckUiController>();
                ui.Initialize(this);

                harmony = new Harmony(PluginId);
                harmony.PatchAll(typeof(InGameHintsPlugin).Assembly);
                Logger.LogInfo("In-game hints loaded. Records: " + repository.Records.Count);
            }
            catch (Exception ex)
            {
                Logger.LogError("In-game hints disabled: " + ex.GetType().Name + ": " + ex.Message);
                enabled = false;
                Cleanup();
            }
        }

        private Book lastActiveBook;
        private bool diagLoggedOnce;
        private float nextHeartbeat;
        private bool firstUpdateLogged;
        private bool firstStartLogged;
        private bool firstEnableLogged;

        private void OnEnable()
        {
            if (!firstEnableLogged) { firstEnableLogged = true; Diag("Plugin.OnEnable"); DumpSelf("OnEnable"); }
        }

        private void Start()
        {
            if (!firstStartLogged) { firstStartLogged = true; Diag("Plugin.Start"); DumpSelf("Start"); }
        }

        private void OnDisable()
        {
            if (ui != null)
                ui.SetBookActive(false, null);
            mouseInputOverrideActive = false;
            // Force a fresh book binding when the component is enabled again.
            lastActiveBook = null;
            if (progressSource != null)
                progressSource.SetBook(null);
            Diag("Plugin.OnDisable"); DumpSelf("OnDisable");
        }

        private void Update()
        {
            if (!firstUpdateLogged)
            {
                firstUpdateLogged = true;
                Diag("Plugin.Update FIRST");
                DumpSelf("Update");
            }

            if (Time.unscaledTime >= nextHeartbeat)
            {
                nextHeartbeat = Time.unscaledTime + 5f;
                bool book = false, game = false;
                try { book = Book.active != null; } catch { }
                try { game = Game.instance != null; } catch { }
                Diag("heartbeat t=" + Time.unscaledTime.ToString("0")
                    + " Game.instance=" + game + " Book.active=" + book + " uiReady=" + (ui != null));
            }

            Book current = null;
            try { current = Book.active; }
            catch { }

            if (current != lastActiveBook)
            {
                if (lastActiveBook != null)
                    OnBookHidden(lastActiveBook);
                if (current != null)
                {
                    if (!diagLoggedOnce)
                    {
                        Diag("Book.active became non-null; showing duck entry.");
                        diagLoggedOnce = true;
                    }
                    OnBookShown(current);
                }
            }
            lastActiveBook = current;

            if (ui != null)
                ui.Tick();
        }

        private void OnDestroy()
        {
            Diag("Plugin.OnDestroy"); DumpSelf("OnDestroy");
            Cleanup();
        }

        private void Cleanup()
        {
            if (ui != null)
                ui.SetBookActive(false, null);
            mouseInputOverrideActive = false;
            if (harmony != null)
            {
                harmony.UnpatchSelf();
                harmony = null;
            }
            if (uiHost != null)
            {
                Destroy(uiHost);
                uiHost = null;
                ui = null;
            }
            if (Instance == this)
                Instance = null;
        }

        internal void OnProfileLoaded(string id)
        {
            if (progressSource == null)
                return;
            // A candidate holds the old save's progress and must not survive a load.
            if (ui != null)
                ui.SetBookActive(false, null);
            lastActiveBook = null;
            progressSource.SetBook(null);
            progressSource.SetActiveProfile(id);
            if (engine != null)
                engine.ClearSessionExclusions();
        }

        private void OnApplicationQuit()
        {
            Diag("Plugin.OnApplicationQuit");
        }

        internal void SetShortcut(KeyCode key)
        {
            shortcutEntry.Value = key == KeyCode.None
                ? KeyboardShortcut.Empty
                : new KeyboardShortcut(key, new KeyCode[0]);
            Config.Save();
        }

        internal void CycleShortcut()
        {
            KeyCode current = shortcutEntry.Value.MainKey;
            KeyCode next;
            if (current == KeyCode.H) next = KeyCode.F1;
            else if (current == KeyCode.F1) next = KeyCode.F2;
            else if (current == KeyCode.F2) next = KeyCode.F8;
            else if (current == KeyCode.F8) next = KeyCode.None;
            else next = KeyCode.H;
            SetShortcut(next);
        }

        internal string ShortcutLabel
        {
            get { return shortcutEntry.Value.MainKey == KeyCode.None ? "无" : shortcutEntry.Value.MainKey.ToString(); }
        }

        internal void OnBookShown(Book book)
        {
            if (progressSource == null || ui == null)
                return;
            Diag("Book shown. book=" + (book != null));
            progressSource.SetBook(book);
            engine.ClearSessionExclusions();
            ui.SetBookActive(true, book);
        }

        internal void OnBookHidden(Book book)
        {
            if (progressSource == null || ui == null)
                return;
            Diag("Book hidden.");
            ui.SetBookActive(false, book);
            progressSource.SetBook(null);
        }

        internal void OnFaceTarget(string faceId)
        {
            if (progressSource != null)
                progressSource.SetCurrentFace(faceId);
        }

        internal void OnCrewTarget(string crewId)
        {
            if (progressSource == null || repository == null)
                return;
            try
            {
                if (SaveData.it != null)
                {
                    SaveData.FaceDataRo face = SaveData.it.FindFaceDataRoForNameId(crewId);
                    if (face != null && !string.IsNullOrEmpty(face.id))
                    {
                        progressSource.SetCurrentFace(face.id);
                        return;
                    }
                }
            }
            catch { }
            // OpenFromMuster supplies a game ID. If it is not assigned in the save yet,
            // it is still the stable face ID produced by Manifest.GetCrewId(index).
            progressSource.SetCurrentFace(crewId);
        }

        internal void PlayHintSound()
        {
            if (!SoundEnabled || progressSource == null || progressSource.ActiveBook == null)
                return;
            try
            {
                BookHelp help = AccessTools.Field(typeof(Book), "help").GetValue(progressSource.ActiveBook) as BookHelp;
                if (help != null)
                    help.PlayNextAudioClip();
            }
            catch { }
        }

        private void LogWarning(string message)
        {
            Logger.LogWarning(message);
        }
    }

    [HarmonyPatch(typeof(Game), "LoadSave")]
    internal static class GameLoadSavePatch
    {
        private static void Postfix(string id)
        {
            if (InGameHintsPlugin.Instance != null)
                InGameHintsPlugin.Instance.OnProfileLoaded(id);
        }
    }

    [HarmonyPatch(typeof(RInput), "UpdateMousePosition")]
    internal static class RInputUpdateMousePositionPatch
    {
        // Keep the original method running, but force its focus branch to leave
        // the system cursor unlocked while the in-game hint panel is open.
        private static void Prefix(ref bool __0)
        {
            if (InGameHintsPlugin.MouseInputOverrideActive)
                __0 = false;
        }
    }

    [HarmonyPatch(typeof(RInput), "get_mousePosition")]
    internal static class RInputMousePositionPatch
    {
        // CustomPointerInputModule consumes RInput.mousePosition. During the
        // panel, use the physical Rewired position so clicks follow the pointer.
        private static void Postfix(ref Vector2 __result)
        {
            if (!InGameHintsPlugin.MouseInputOverrideActive)
                return;
            try { __result = RInput.physicalMousePosition; }
            catch { }
        }
    }

    // Book show/hide is detected by polling Book.active in Update (see InGameHintsPlugin),
    // because Book.OnEnable does not re-fire when the journal is reopened in the office scene.

    [HarmonyPatch(typeof(FateEditor), "OpenFromFace")]
    internal static class FateEditorOpenFromFacePatch
    {
        private static void Postfix(string faceId)
        {
            if (InGameHintsPlugin.Instance != null)
                InGameHintsPlugin.Instance.OnFaceTarget(faceId);
        }
    }

    [HarmonyPatch(typeof(FateEditor), "OpenFromMuster")]
    internal static class FateEditorOpenFromMusterPatch
    {
        private static void Postfix(string nameId)
        {
            if (InGameHintsPlugin.Instance != null)
                InGameHintsPlugin.Instance.OnCrewTarget(nameId);
        }
    }
}
