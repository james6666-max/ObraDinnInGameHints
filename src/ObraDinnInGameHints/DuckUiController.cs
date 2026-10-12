using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ObraDinnInGameHints
{
    public sealed class DuckUiController : MonoBehaviour
    {
        private enum ViewMode
        {
            Hint,
            WarningOne,
            WarningTwo,
            FinalShown,
            Settings,
            ResetWarning,
            Disabled
        }

        private sealed class ButtonView
        {
            public GameObject GameObject;
            public Button Button;
            public Text Label;
        }

        private InGameHintsPlugin plugin;
        private Canvas canvas;
        private ButtonView duckButton;
        private GameObject panel;
        private GameObject modalBlocker;
        private Image contentDivider;
        private Image actionDivider;
        private Image faceImage;
        private Text titleText;
        private Text bodyText;
        private Text progressText;
        private ButtonView primaryButton;
        private ButtonView secondaryButton;
        private ButtonView tertiaryButton;
        private ButtonView settingsButton;
        private ButtonView closeButton;
        private readonly List<Text> allTexts = new List<Text>();

        private Book activeBook;
        private bool bookActive;
        private bool panelOpen;
        private HintCandidate candidate;
        private ViewMode mode;
        private string transientMessage;
        private EventSystem ownedEventSystem;
        private MouseCursor nativeMouseCursor;
        private Image nativeCursorImage;
        private bool nativeCursorStateCaptured;
        private bool nativeCursorOriginalEnabled;
        private bool nativeCursorOriginalImageEnabled;
        private bool nativeCursorOriginalRaycastTarget;
        private bool unityCursorStateCaptured;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLockMode;
        private Texture2D duckTexture;
        private Sprite duckSprite;

        internal void Initialize(InGameHintsPlugin plugin)
        {
            this.plugin = plugin;
            BuildUi();
            canvas.enabled = false;
        }

        internal void SetBookActive(bool active, Book book)
        {
            bookActive = active;
            activeBook = active ? book : null;
            if (active && book != null)
                TryAdoptBookFont(book);
            if (!active)
                ClosePanel();
            if (canvas != null)
                canvas.enabled = active;
            if (!active && ownedEventSystem != null)
                ownedEventSystem.gameObject.SetActive(false);
        }

        internal void Tick()
        {
            if (!bookActive || activeBook == null || canvas == null)
                return;

            EnsureEventSystem();

            bool blocked = false;
            try { blocked = activeBook.inAnim || activeBook.topPopup != null; }
            catch { }
            duckButton.Button.interactable = !blocked || panelOpen;

            KeyboardShortcut shortcut = plugin.Shortcut;
            if ((!blocked || panelOpen) && shortcut.MainKey != KeyCode.None && shortcut.IsDown())
            {
                if (panelOpen) ClosePanel();
                else OpenPanel();
            }

            if (panelOpen && Input.GetKeyDown(KeyCode.Escape))
                ClosePanel();

            if (panelOpen && !InGameHintsPlugin.MouseInputOverrideActive)
                plugin.SetMouseInputOverride(true);

            // The game's MouseCursor may appear after the journal opens, so retry
            // safely until it can be hidden without creating a second pointer.
            if (UiPresentation.ShouldRetryNativeCursor(
                    panelOpen, nativeCursorStateCaptured, nativeMouseCursor != null))
            {
                RestoreNativeCursor();
                TryAdoptNativeCursor();
            }
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject("InGameHintsCanvas");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32700;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            duckButton = CreateButton("DuckButton", canvasObject.transform, "", new Vector2(72f, 72f));
            RectTransform duckRect = duckButton.GameObject.GetComponent<RectTransform>();
            duckRect.anchorMin = new Vector2(1f, 0f);
            duckRect.anchorMax = new Vector2(1f, 0f);
            duckRect.pivot = new Vector2(1f, 0f);
            duckRect.anchoredPosition = new Vector2(-36f, 28f);
            duckButton.Button.onClick.AddListener(OpenPanel);
            AddDuckIcon(duckButton.GameObject.transform);

            modalBlocker = new GameObject("ModalInputBlocker");
            modalBlocker.transform.SetParent(canvasObject.transform, false);
            RectTransform blockerRect = modalBlocker.AddComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = Vector2.zero;
            blockerRect.offsetMax = Vector2.zero;
            Image blockerImage = modalBlocker.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.01f);
            blockerImage.raycastTarget = true;

            panel = CreatePanel(canvasObject.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(900f, 590f);
            panelRect.anchoredPosition = Vector2.zero;

            titleText = CreateText("Title", panel.transform, 30, TextAnchor.MiddleCenter, Color.white);
            SetRect(titleText.rectTransform, new Vector2(0f, 247f), new Vector2(800f, 48f));

            contentDivider = CreateImage("ContentDivider", panel.transform, new Color(1f, 1f, 1f, 0.22f));
            contentDivider.raycastTarget = false;
            SetRect(contentDivider.rectTransform, new Vector2(0f, 211f), new Vector2(790f, 1f));

            faceImage = CreateImage("Face", panel.transform, new Color(1f, 1f, 1f, 1f));
            faceImage.preserveAspect = true;
            faceImage.raycastTarget = false;
            SetRect(faceImage.rectTransform, new Vector2(-330f, 75f), new Vector2(160f, 205f));

            bodyText = CreateText("Body", panel.transform, 21, TextAnchor.UpperLeft, Color.white);
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Truncate;
            bodyText.lineSpacing = 1.16f;
            SetRect(bodyText.rectTransform, new Vector2(92f, 75f), new Vector2(560f, 220f));

            progressText = CreateText("Progress", panel.transform, 16, TextAnchor.MiddleCenter, new Color(0.78f, 0.78f, 0.78f, 1f));
            SetRect(progressText.rectTransform, new Vector2(0f, -77f), new Vector2(780f, 34f));

            actionDivider = CreateImage("ActionDivider", panel.transform, new Color(1f, 1f, 1f, 0.18f));
            actionDivider.raycastTarget = false;
            SetRect(actionDivider.rectTransform, new Vector2(0f, -108f), new Vector2(790f, 1f));

            primaryButton = CreateButton("Primary", panel.transform, "给一点提示", new Vector2(230f, 52f));
            secondaryButton = CreateButton("Secondary", panel.transform, "换一条", new Vector2(160f, 52f));
            tertiaryButton = CreateButton("Tertiary", panel.transform, "", new Vector2(160f, 52f));
            settingsButton = CreateButton("Settings", panel.transform, "设置", new Vector2(130f, 42f));
            closeButton = CreateButton("Close", panel.transform, "关闭", new Vector2(130f, 42f));

            SetRect(primaryButton.GameObject.GetComponent<RectTransform>(), new Vector2(0f, -155f), new Vector2(230f, 52f));
            SetRect(secondaryButton.GameObject.GetComponent<RectTransform>(), new Vector2(0f, -155f), new Vector2(160f, 52f));
            SetRect(tertiaryButton.GameObject.GetComponent<RectTransform>(), new Vector2(0f, -155f), new Vector2(160f, 52f));
            SetRect(settingsButton.GameObject.GetComponent<RectTransform>(), new Vector2(-350f, -245f), new Vector2(130f, 42f));
            SetRect(closeButton.GameObject.GetComponent<RectTransform>(), new Vector2(350f, -245f), new Vector2(130f, 42f));

            primaryButton.Button.onClick.AddListener(OnPrimary);
            secondaryButton.Button.onClick.AddListener(OnSecondary);
            tertiaryButton.Button.onClick.AddListener(OnTertiary);
            settingsButton.Button.onClick.AddListener(OnSettings);
            closeButton.Button.onClick.AddListener(OnClose);

            panel.SetActive(false);
            modalBlocker.SetActive(false);

        }

        private void OpenPanel()
        {
            if (!bookActive)
                return;
            panelOpen = true;
            CaptureAndEnableMouseInput();
            TryAdoptNativeCursor();
            modalBlocker.SetActive(true);
            panel.SetActive(true);
            transientMessage = null;

            if (!plugin.HintsEnabled)
            {
                mode = ViewMode.Disabled;
                RenderDisabled();
            }
            else
            {
                mode = ViewMode.Hint;
                candidate = plugin.Engine.SelectCandidate();
                LogCandidateDiag();
                RenderHint();
            }
            FocusFirstAvailable();
        }

        private void ClosePanel()
        {
            panelOpen = false;
            candidate = null;
            transientMessage = null;
            if (panel != null)
                panel.SetActive(false);
            if (modalBlocker != null)
                modalBlocker.SetActive(false);
            DisableMouseInputAndRestoreCursor();
            RestoreNativeCursor();
            if (EventSystem.current != null && duckButton != null && duckButton.GameObject.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(duckButton.GameObject);
        }

        private void LogCandidateDiag()
        {
            try
            {
                if (candidate == null)
                {
                    plugin.DiagLog("candidate: NONE (no safe recommendation)");
                    return;
                }
                SubjectProgress p = candidate.Progress;
                plugin.DiagLog("candidate: branch=" + candidate.Branch
                    + " visible=" + p.Visible
                    + " markedCorrect=" + p.MarkedCorrect
                    + " identityResolved=" + p.IdentityResolved
                    + " fateResolved=" + p.FateResolved
                    + " isCurrent=" + p.IsCurrent
                    + " clues=" + p.VisitedClues + "/" + p.TotalClues
                    + " revealed=" + candidate.HintProgress.Revealed
                    + " score=" + candidate.Score);
            }
            catch { }
        }

        private void RenderHint()
        {
            ResetButtons();
            if (candidate == null)
            {
                faceImage.gameObject.SetActive(false);
                titleText.text = "暂时没有安全提示";
                bodyText.text = "当前日志里没有可可靠推荐、且尚未解决的目标。继续探索新的回忆，或稍后再来问小黄鸭。";
                progressText.text = "提示按调查进度推荐，仍可能涉及尚未注意到的线索";
                Configure(secondaryButton, "重新检查", true);
                Configure(settingsButton, "设置", true);
                Configure(closeButton, "关闭", true);
                RefreshLayout(false);
                return;
            }

            Sprite sprite = plugin.ProgressSource.GetFaceSprite(candidate.Progress.GameFaceId);
            faceImage.gameObject.SetActive(sprite != null);
            faceImage.sprite = sprite;
            titleText.text = candidate.Branch == HintBranch.Identity ? "身份线索" : "下落线索";

            string latest = plugin.Engine.GetLatestHint(candidate);
            if (string.IsNullOrEmpty(latest))
            {
                bodyText.text = "先别急着看答案。请用一句话说说：你已经确认了什么？还缺少哪一步证据？\n\n如果仍然卡住，小黄鸭再给你第一层提示。";
                Configure(primaryButton, "还是给我提示", true);
            }
            else
            {
                bodyText.text = latest;
                ConfigureNextAction();
            }

            int unlocked = plugin.Engine.GetUnlockedNonFinal(candidate);
            string clueProgress = candidate.Progress.TotalClues == 0
                ? "相关回忆：无需额外门槛"
                : "相关回忆：" + candidate.Progress.VisitedClues + "/" + candidate.Progress.TotalClues;
            progressText.text = clueProgress + "    已查看普通提示：" + candidate.HintProgress.Revealed + "/" + candidate.NonFinalCount;
            if (!string.IsNullOrEmpty(transientMessage))
                progressText.text = transientMessage + "    " + progressText.text;

            Configure(secondaryButton, "换一条", true);
            Configure(settingsButton, "设置", true);
            Configure(closeButton, "关闭", true);
            RefreshLayout(sprite != null);
        }

        private void ConfigureNextAction()
        {
            int unlocked = plugin.Engine.GetUnlockedNonFinal(candidate);
            if (candidate.HintProgress.Revealed < unlocked)
                Configure(primaryButton, "再给一点提示", true);
            else if (candidate.HintProgress.Revealed < candidate.NonFinalCount)
                Configure(primaryButton, "继续调查后解锁", false);
            else if (candidate.AllRelevantCluesVisited)
                Configure(primaryButton, "查看最终提示", true);
            else
                Configure(primaryButton, "看完相关回忆后解锁", false);
        }

        private void RenderWarningOne()
        {
            ResetButtons();
            faceImage.gameObject.SetActive(false);
            titleText.text = "剧透警告";
            bodyText.text = "下一层会直接给出结论。\n\n它不会自动填写游戏日志，也不会影响成就，但会失去自己推理出答案的机会。";
            progressText.text = "这是第一次确认";
            Configure(primaryButton, "继续", true);
            Configure(secondaryButton, "返回提示", true);
            Configure(closeButton, "关闭", true);
            RefreshLayout(false);
        }

        private void RenderWarningTwo()
        {
            ResetButtons();
            faceImage.gameObject.SetActive(false);
            titleText.text = "再次确认";
            bodyText.text = "确定要查看这个目标的最终提示吗？\n\n显示后，提示系统会记住你已经看过，但仍不会修改游戏里的任何填写内容。";
            progressText.text = "这是最后一次确认";
            Configure(primaryButton, "我确定要看答案", true);
            Configure(secondaryButton, "返回提示", true);
            Configure(closeButton, "关闭", true);
            RefreshLayout(false);
        }

        private void RenderFinal(string text)
        {
            ResetButtons();
            faceImage.gameObject.SetActive(false);
            titleText.text = "最终提示";
            bodyText.text = text;
            progressText.text = "答案仅显示在提示面板中，游戏日志未被修改";
            Configure(secondaryButton, "换一条", true);
            Configure(settingsButton, "设置", true);
            Configure(closeButton, "关闭", true);
            RefreshLayout(false);
        }

        private void RenderSettings()
        {
            ResetButtons();
            faceImage.gameObject.SetActive(false);
            titleText.text = "提示设置";
            bodyText.text = "设置只影响这个模组。提示进度与游戏存档分开保存。\n\n停用后，小黄鸭入口仍保留，方便重新启用。";
            progressText.text = "当前存档：" + plugin.ProgressSource.ActiveProfileId;
            Configure(primaryButton, plugin.HintsEnabled ? "智能提示：开启" : "智能提示：暂停", true);
            Configure(secondaryButton, "快捷键：" + plugin.ShortcutLabel, true);
            Configure(tertiaryButton, plugin.SoundEnabled ? "音效：开启" : "音效：关闭", true);
            Configure(settingsButton, "重置本存档", true);
            Configure(closeButton, "返回", true);
            RefreshLayout(false);
        }

        private void RenderResetWarning()
        {
            ResetButtons();
            faceImage.gameObject.SetActive(false);
            titleText.text = "重置提示进度";
            bodyText.text = "确定清除当前存档槽的所有已查看提示和最终提示记录吗？\n\n这不会修改游戏存档，但此操作不可撤销。";
            progressText.text = "当前存档：" + plugin.ProgressSource.ActiveProfileId;
            Configure(primaryButton, "确认清除", true);
            Configure(secondaryButton, "取消", true);
            Configure(closeButton, "关闭", true);
            RefreshLayout(false);
        }

        private void RenderDisabled()
        {
            ResetButtons();
            faceImage.gameObject.SetActive(false);
            titleText.text = "智能提示已暂停";
            bodyText.text = "小黄鸭目前不会分析或推荐目标。你可以在设置中重新启用。";
            progressText.text = "快捷键仍可打开此面板";
            Configure(settingsButton, "设置", true);
            Configure(closeButton, "关闭", true);
            RefreshLayout(false);
        }

        private void OnPrimary()
        {
            try { OnPrimaryCore(); }
            catch (IOException ex) { ShowStateSaveFailure(ex); }
            catch (UnauthorizedAccessException ex) { ShowStateSaveFailure(ex); }
        }

        private void ShowStateSaveFailure(Exception error)
        {
            // State writes roll back on failure; keep the current view for retry.
            progressText.text = "无法保存提示记录，请检查配置目录权限或文件占用后重试";
            plugin.DiagLog("Hint state save failed: " + error.GetType().Name);
        }

        private void OnPrimaryCore()
        {
            if (mode == ViewMode.WarningOne)
            {
                mode = ViewMode.WarningTwo;
                RenderWarningTwo();
                FocusFirstAvailable();
                return;
            }
            if (mode == ViewMode.WarningTwo)
            {
                AdvanceResult final = plugin.Engine.RevealFinalHint(candidate);
                mode = ViewMode.FinalShown;
                if (final.Kind == AdvanceResultKind.FinalHint)
                {
                    plugin.PlayHintSound();
                    RenderFinal(final.Text);
                }
                else
                {
                    mode = ViewMode.Hint;
                    transientMessage = "相关回忆尚未全部访问";
                    RenderHint();
                }
                FocusFirstAvailable();
                return;
            }
            if (mode == ViewMode.Settings)
            {
                plugin.HintsEnabled = !plugin.HintsEnabled;
                RenderSettings();
                return;
            }
            if (mode == ViewMode.ResetWarning)
            {
                plugin.Engine.ResetCurrentProfile();
                mode = ViewMode.Settings;
                RenderSettings();
                return;
            }
            if (mode != ViewMode.Hint || candidate == null)
                return;

            AdvanceResult result = plugin.Engine.RequestNextHint(candidate);
            if (result.Kind == AdvanceResultKind.Hint)
            {
                plugin.PlayHintSound();
                transientMessage = null;
                RenderHint();
            }
            else if (result.Kind == AdvanceResultKind.Locked)
            {
                transientMessage = "更明确的提示尚未解锁";
                RenderHint();
            }
            else if (result.Kind == AdvanceResultKind.FinalConfirmationRequired)
            {
                mode = ViewMode.WarningOne;
                RenderWarningOne();
            }
            else if (result.Kind == AdvanceResultKind.NothingAvailable)
            {
                candidate = plugin.Engine.SelectCandidate();
                transientMessage = null;
                RenderHint();
            }
            FocusFirstAvailable();
        }

        private void OnSecondary()
        {
            if (mode == ViewMode.WarningOne || mode == ViewMode.WarningTwo)
            {
                mode = ViewMode.Hint;
                RenderHint();
                FocusFirstAvailable();
                return;
            }
            if (mode == ViewMode.Settings)
            {
                plugin.CycleShortcut();
                RenderSettings();
                return;
            }
            if (mode == ViewMode.ResetWarning)
            {
                mode = ViewMode.Settings;
                RenderSettings();
                return;
            }

            if (candidate != null)
                plugin.Engine.ExcludeForSession(candidate);
            candidate = plugin.Engine.SelectCandidate();
            mode = ViewMode.Hint;
            transientMessage = null;
            RenderHint();
            FocusFirstAvailable();
        }

        private void OnTertiary()
        {
            if (mode == ViewMode.Settings)
            {
                plugin.SoundEnabled = !plugin.SoundEnabled;
                RenderSettings();
            }
        }

        private void OnSettings()
        {
            if (mode == ViewMode.Settings)
            {
                mode = ViewMode.ResetWarning;
                RenderResetWarning();
            }
            else
            {
                mode = ViewMode.Settings;
                RenderSettings();
            }
            FocusFirstAvailable();
        }

        private void OnClose()
        {
            if (mode == ViewMode.Settings || mode == ViewMode.ResetWarning)
            {
                if (!plugin.HintsEnabled)
                {
                    mode = ViewMode.Disabled;
                    RenderDisabled();
                }
                else
                {
                    mode = ViewMode.Hint;
                    candidate = plugin.Engine.SelectCandidate();
                    RenderHint();
                }
                FocusFirstAvailable();
            }
            else ClosePanel();
        }

        private void ResetButtons()
        {
            Configure(primaryButton, "", false, false);
            Configure(secondaryButton, "", false, false);
            Configure(tertiaryButton, "", false, false);
            Configure(settingsButton, "", false, false);
            Configure(closeButton, "", false, false);
        }

        private static void Configure(ButtonView view, string label, bool interactable)
        {
            Configure(view, label, interactable, true);
        }

        private static void Configure(ButtonView view, string label, bool interactable, bool visible)
        {
            view.GameObject.SetActive(visible);
            view.Label.text = label;
            view.Button.interactable = interactable;
        }

        private void FocusFirstAvailable()
        {
            EnsureEventSystem();
            if (EventSystem.current == null)
                return;
            ButtonView[] order = { primaryButton, secondaryButton, tertiaryButton, settingsButton, closeButton };
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i].GameObject.activeInHierarchy && order[i].Button.interactable)
                {
                    EventSystem.current.SetSelectedGameObject(order[i].GameObject);
                    return;
                }
            }
        }

        private void RefreshLayout(bool showFace)
        {
            SetRect(
                bodyText.rectTransform,
                showFace ? new Vector2(92f, 75f) : new Vector2(0f, 75f),
                showFace ? new Vector2(560f, 220f) : new Vector2(760f, 220f));

            List<ButtonView> visibleActions = new List<ButtonView>();
            if (primaryButton.GameObject.activeSelf) visibleActions.Add(primaryButton);
            if (secondaryButton.GameObject.activeSelf) visibleActions.Add(secondaryButton);
            if (tertiaryButton.GameObject.activeSelf) visibleActions.Add(tertiaryButton);

            List<float> widths = new List<float>();
            for (int i = 0; i < visibleActions.Count; i++)
                widths.Add(visibleActions[i].GameObject.GetComponent<RectTransform>().sizeDelta.x);
            float[] positions = UiPresentation.CenterButtonPositions(widths, 18f);
            for (int i = 0; i < visibleActions.Count; i++)
            {
                RectTransform rect = visibleActions[i].GameObject.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(positions[i], -155f);
            }
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            if (ownedEventSystem != null)
            {
                ownedEventSystem.gameObject.SetActive(true);
                ownedEventSystem.enabled = true;
                return;
            }

            GameObject go = new GameObject("InGameHintsEventSystem");
            go.transform.SetParent(transform, false);
            ownedEventSystem = go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        private void CaptureAndEnableMouseInput()
        {
            if (!unityCursorStateCaptured)
            {
                previousCursorVisible = Cursor.visible;
                previousCursorLockMode = Cursor.lockState;
                unityCursorStateCaptured = true;
            }

            if (UiPresentation.ShouldOverrideMouseInput(panelOpen))
                plugin.SetMouseInputOverride(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void DisableMouseInputAndRestoreCursor()
        {
            if (plugin != null)
                plugin.SetMouseInputOverride(false);
            if (!UiPresentation.ShouldRestoreMouseState(false, unityCursorStateCaptured))
                return;

            try
            {
                Cursor.lockState = previousCursorLockMode;
                Cursor.visible = previousCursorVisible;
            }
            catch { }
            finally
            {
                unityCursorStateCaptured = false;
            }
        }

        private void TryAdoptNativeCursor()
        {
            if (!panelOpen || nativeCursorStateCaptured)
                return;

            MouseCursor found = null;
            try { found = UnityEngine.Object.FindObjectOfType<MouseCursor>(); }
            catch { return; }
            if (found == null)
                return;

            try
            {
                Image cursorImage = found.GetComponent<Image>();
                if (cursorImage == null)
                    cursorImage = found.GetComponentInChildren<Image>(true);

                nativeMouseCursor = found;
                nativeCursorImage = cursorImage;
                nativeCursorOriginalEnabled = found.enabled;
                nativeCursorOriginalImageEnabled = cursorImage != null && cursorImage.enabled;
                nativeCursorOriginalRaycastTarget = cursorImage != null && cursorImage.raycastTarget;
                nativeCursorStateCaptured = true;

                // Pause the game's rendered pointer while the system pointer and
                // patched RInput coordinates drive the hint panel.
                found.enabled = false;
                if (cursorImage != null)
                {
                    cursorImage.enabled = false;
                    cursorImage.raycastTarget = false;
                }
            }
            catch
            {
                RestoreNativeCursor();
            }
        }

        private void RestoreNativeCursor()
        {
            if (!nativeCursorStateCaptured)
                return;

            try
            {
                if (nativeMouseCursor != null)
                {
                    nativeMouseCursor.enabled = nativeCursorOriginalEnabled;
                }
                if (nativeCursorImage != null)
                {
                    nativeCursorImage.enabled = nativeCursorOriginalImageEnabled;
                    nativeCursorImage.raycastTarget = nativeCursorOriginalRaycastTarget;
                }
            }
            catch { }
            finally
            {
                nativeMouseCursor = null;
                nativeCursorImage = null;
                nativeCursorStateCaptured = false;
                nativeCursorOriginalEnabled = false;
                nativeCursorOriginalImageEnabled = false;
                nativeCursorOriginalRaycastTarget = false;
            }
        }

        private void OnDisable()
        {
            ClosePanel();
        }

        private void OnDestroy()
        {
            DisableMouseInputAndRestoreCursor();
            RestoreNativeCursor();
            if (duckSprite != null)
                Destroy(duckSprite);
            if (duckTexture != null)
                Destroy(duckTexture);
        }

        private void TryAdoptBookFont(Book book)
        {
            try
            {
                Text[] texts = book.GetComponentsInChildren<Text>(true);
                Font selected = null;
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i].font != null)
                    {
                        selected = texts[i].font;
                        break;
                    }
                }
                if (selected == null)
                    return;
                for (int i = 0; i < allTexts.Count; i++)
                    allTexts[i].font = selected;
            }
            catch { }
        }

        private GameObject CreatePanel(Transform parent)
        {
            GameObject go = new GameObject("HintPanel");
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.02f, 0.02f, 0.02f, 0.97f);
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(3f, -3f);
            return go;
        }

        private ButtonView CreateButton(string name, Transform parent, string label, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            Image image = go.AddComponent<Image>();
            image.color = Color.white;
            Button button = go.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
            colors.disabledColor = new Color(0.28f, 0.28f, 0.28f, 0.9f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };

            Text text = CreateText("Label", go.transform, 20, TextAnchor.MiddleCenter, Color.black);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(8f, 4f);
            text.rectTransform.offsetMax = new Vector2(-8f, -4f);
            text.text = label;

            return new ButtonView { GameObject = go, Button = button, Label = text };
        }

        private Text CreateText(string name, Transform parent, int size, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Text text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.supportRichText = false;
            allTexts.Add(text);
            return text;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private void AddDuckIcon(Transform parent)
        {
            try
            {
                Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ObraDinnInGameHints.Resources.duck.png");
                if (stream == null)
                {
                    duckButton.Label.text = "?";
                    return;
                }
                byte[] bytes;
                using (stream)
                using (MemoryStream memory = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        memory.Write(buffer, 0, read);
                    bytes = memory.ToArray();
                }
                Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                duckTexture = texture;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.LoadImage(bytes);
                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                duckSprite = sprite;
                Image image = CreateImage("DuckIcon", parent, Color.black);
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.rectTransform.anchorMin = new Vector2(0.12f, 0.12f);
                image.rectTransform.anchorMax = new Vector2(0.88f, 0.88f);
                image.rectTransform.offsetMin = Vector2.zero;
                image.rectTransform.offsetMax = Vector2.zero;
            }
            catch
            {
                duckButton.Label.text = "?";
            }
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
