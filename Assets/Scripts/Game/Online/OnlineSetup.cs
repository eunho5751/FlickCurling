using System.Threading;
using Cysharp.Threading.Tasks;
using FlickCurling.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace FlickCurling.Game.Online
{
    /// <summary>
    /// The popup the main menu's "Online Match" button opens (built by MainMenuBuilder on the shared Popup, like the
    /// offline one): the opponent (random, or a friend) and the map (mode).
    /// Random: OK starts looking for an opponent; while it looks, the button shows the time spent ("00:00"), and pressing
    /// it again, choosing another map (mode) or closing the popup stops. Once an opponent is found the match can no longer be cancelled: the Game
    /// scene opens on it (and if the two cannot be connected there, the menu comes back with this popup looking again).
    /// A friend: "create room" opens a private room on the chosen map and shows its code (copy, share; cancel closes it;
    /// GameConfig.friendRoomMinutes without the friend and it closes itself), "enter code" joins a friend's room by its
    /// code (the room's map is played). Either way the Game scene opens once both are in, and only on the same version of
    /// the game (a friend on another one is told so).
    /// Online matches need the newest build: when Google Play has a newer one (<see cref="AppUpdate"/>), the popup shows
    /// only that, with "update" (Play's update screen).
    /// </summary>
    public sealed class OnlineSetup : MonoBehaviour
    {
        [SerializeField] Popup popup;
        [SerializeField] SegmentedToggle mode;   // random, friend
        [SerializeField] SegmentedToggle map;

        // the maps (modes), in the toggle's order
        static readonly string[] MapKeys = { "base", Maps.ItemKey };
        [SerializeField] Button ok;

        [Header("A friend")]
        [SerializeField] GameObject main;        // the choices (the view the popup opens on)
        [SerializeField] GameObject okRow, friendRow;
        [SerializeField] Button createRoom, enterCode;
        [SerializeField] GameObject room;        // the room made, waiting for the friend
        [SerializeField] TextMeshProUGUI code, roomStatus;
        [SerializeField] Button copy, share, cancel;
        [SerializeField] GameObject join;        // a friend's code entered
        [SerializeField] TMP_InputField codeInput;
        [SerializeField] TextMeshProUGUI joinStatus;
        [SerializeField] Button joinOk, back;

        [Header("An update to take first")]
        [SerializeField] GameObject update;
        [SerializeField] Button updateNow;

        const int CodeLength = 6;
        const float CopiedShow = 2, OtherVersionShow = 5;

        CancellationTokenSource search;
        float started;
        TextMeshProUGUI label;
        LocalizeStringEvent labelLoc;

        CancellationTokenSource roomCts;   // a room open (or opening), waiting for the friend
        string roomCode;
        float roomSince, copiedUntil, otherVersionUntil;
        bool roomGone;                      // closed by itself, or could not be opened: its line stays
        bool joining;

        void Awake()
        {
            ok.onClick.AddListener(Toggle);
            popup.Closed += Stop;
            label = ok.GetComponentInChildren<TextMeshProUGUI>(true);
            labelLoc = ok.GetComponentInChildren<LocalizeStringEvent>(true);
            mode.Changed += _ => ShowChoice();
            map.Changed += _ => Stop();   // another mode while looking: that search is off (OK starts one for the new mode)
            createRoom.onClick.AddListener(CreateRoom);
            enterCode.onClick.AddListener(EnterCode);
            copy.onClick.AddListener(Copy);
            share.onClick.AddListener(Share);
            cancel.onClick.AddListener(() => { CloseRoom(); Show(main); });
            joinOk.onClick.AddListener(Join);
            back.onClick.AddListener(() => Show(main));
            updateNow.onClick.AddListener(UpdateNow);
            // the code as typed: letters and digits only, in capitals
            codeInput.onValidateInput = (_, __, c) => char.IsLetterOrDigit(c) && c < 128 ? char.ToUpperInvariant(c) : '\0';
            codeInput.onSubmit.AddListener(_ => Join());
        }

        void OnDestroy() => Stop();

        public void Open()
        {
            OnlineLobby.MeasureRegion();   // ahead of a match (in the background)
            AppUpdate.Check();             // (asked again each time: an update put out meanwhile)
            mode.Select(0, false, false);
            map.Select(0, false, false);
            Stop();
            ok.interactable = true;
            updateNow.interactable = true;
            Show(NeedsUpdate ? update : main);
            popup.SetSize(860, 764);
            popup.Show();
        }

        /// <summary>Opens the popup already looking (back from a match that could not be connected).</summary>
        public void OpenSearching()
        {
            Open();
            if (NeedsUpdate) return;
            Toggle();
            OnlineLobby.Trace("search started again (match not connected)");
        }

        void Show(GameObject view)
        {
            main.SetActive(view == main);
            room.SetActive(view == room);
            join.SetActive(view == join);
            update.SetActive(view == update);
            if (view == main) ShowChoice();
        }

        // random: OK; a friend: create a room or enter a code
        void ShowChoice()
        {
            bool friend = mode.Selected == 1;
            if (friend) Stop();
            okRow.SetActive(!friend);
            friendRow.SetActive(friend);
        }

        string Map => MapKeys[Mathf.Clamp(map.Selected, 0, MapKeys.Length - 1)];

        // ---------------- random ----------------

        void Toggle()
        {
            if (search != null) { Stop(); return; }
            search = new CancellationTokenSource();
            started = Time.unscaledTime;
            OnlineLobby.Trace("search started (online popup)");
            if (labelLoc) labelLoc.enabled = false;   // the time replaces "OK" until the search stops
            Tick();
            Find(search).Forget();
        }

        async UniTaskVoid Find(CancellationTokenSource cts)
        {
            OnlineLobby.SearchMap = Map;
            bool found = await OnlineLobby.FindAsync(cts.Token, retry: true);
            if (!this || search != cts) return;
            search = null;
            if (!found) { Restore(); return; }
            Go();
        }

        void Go()
        {
            ok.interactable = false;
            MatchLaunch.SetOnline();
            ScreenFader.LoadScene(MatchLaunch.GameScene);
        }

        void Stop()
        {
            CloseRoom();
            if (OnlineLobby.Joining) return;   // a match found is no longer cancelled
            if (search == null) return;
            OnlineLobby.Trace("search cancelled (online popup)");
            search.Cancel();
            search = null;
            Restore();
        }

        void Restore()
        {
            if (labelLoc) { labelLoc.enabled = true; labelLoc.RefreshString(); }
        }

        void Update()
        {
            if (search != null) Tick();
            if (room.activeSelf) TickRoom();
            // Play's answer came after the popup opened: an update first, unless already looking or in a room
            if (NeedsUpdate && main.activeSelf && search == null && roomCts == null) Show(update);
        }

        // ---------------- an update to take first ----------------

        static bool NeedsUpdate => AppUpdate.Status == AppUpdate.State.Available;

        async void UpdateNow()
        {
            updateNow.interactable = false;
            await AppUpdate.StartAsync();   // (taken: the app restarts on the new build)
            if (this) updateNow.interactable = true;
        }

        void Tick()
        {
            int t = Mathf.FloorToInt(Time.unscaledTime - started);
            string text = $"{t / 60:00}:{t % 60:00}";
            if (label && label.text != text) label.text = text;
        }

        // ---------------- a friend's room ----------------

        void CreateRoom()
        {
            CloseRoom();
            roomCts = new CancellationTokenSource();
            roomCode = null; roomGone = false; copiedUntil = otherVersionUntil = 0;
            code.text = new string('-', CodeLength);
            roomStatus.text = Loc.T("online.creatingRoom");
            copy.interactable = share.interactable = false;
            Show(room);
            OpenRoom(roomCts).Forget();
        }

        async UniTaskVoid OpenRoom(CancellationTokenSource cts)
        {
            var result = await OnlineLobby.CreateRoomAsync(Map, c =>
            {
                if (!this || roomCts != cts) return;
                roomCode = c; roomSince = Time.unscaledTime;
                code.text = c;
                copy.interactable = share.interactable = true;
            }, () =>
            {
                if (this && roomCts == cts) otherVersionUntil = Time.unscaledTime + OtherVersionShow;
            }, cts.Token);
            if (!this || roomCts != cts) return;
            roomCts = null;
            switch (result)
            {
                case OnlineLobby.RoomResult.Started: Go(); return;
                case OnlineLobby.RoomResult.Cancelled: return;
            }
            // closed by itself (nobody came) or not opened: said where the code was
            roomGone = true;
            roomCode = null;
            code.text = new string('-', CodeLength);
            copy.interactable = share.interactable = false;
            roomStatus.text = Loc.T(result == OnlineLobby.RoomResult.Expired ? "online.roomExpired" : "online.roomFailed");
        }

        // the room given up (cancel, the popup closed, another choice): left, unless the friend is already coming in
        void CloseRoom()
        {
            if (roomCts == null || OnlineLobby.Joining) return;
            roomCts.Cancel();
            roomCts = null;
        }

        void TickRoom()
        {
            cancel.interactable = !OnlineLobby.Joining;
            if (roomGone || roomCode == null) return;
            string text;
            if (Time.unscaledTime < otherVersionUntil) text = Loc.T("online.otherVersion");
            else if (Time.unscaledTime < copiedUntil) text = Loc.T("online.copied");
            else
            {
                int t = Mathf.FloorToInt(Time.unscaledTime - roomSince);
                text = Loc.T("online.waitingFriend", ("time", $"{t / 60:00}:{t % 60:00}"));
            }
            if (roomStatus.text != text) roomStatus.text = text;
        }

        void Copy()
        {
            if (string.IsNullOrEmpty(roomCode)) return;
            GUIUtility.systemCopyBuffer = roomCode;
            copiedUntil = Time.unscaledTime + CopiedShow;
        }

        void Share()
        {
            if (string.IsNullOrEmpty(roomCode)) return;
            NativeShare.Text(Loc.T("online.shareText", ("code", roomCode)), Loc.T("online.share"));
        }

        // ---------------- joining a friend's room ----------------

        void EnterCode()
        {
            codeInput.text = "";
            joinStatus.text = "";
            SetJoining(false);
            Show(join);
            codeInput.ActivateInputField();
        }

        void Join()
        {
            if (joining) return;
            string c = codeInput.text.Trim().ToUpperInvariant();
            if (c.Length != CodeLength) { joinStatus.text = Loc.T("online.codeLength"); return; }
            JoinRoom(c).Forget();
        }

        async UniTaskVoid JoinRoom(string c)
        {
            SetJoining(true);
            joinStatus.text = Loc.T("online.joiningRoom");
            var result = await OnlineLobby.JoinRoomAsync(c);
            if (!this) return;
            if (result == OnlineLobby.JoinResult.Started) { Go(); return; }
            SetJoining(false);
            joinStatus.text = Loc.T(result == OnlineLobby.JoinResult.NotFound ? "online.roomNotFound"
                : result == OnlineLobby.JoinResult.Full ? "online.roomFull"
                : result == OnlineLobby.JoinResult.Version ? "online.otherVersion" : "online.joinFailed");
        }

        void SetJoining(bool on)
        {
            joining = on;
            joinOk.interactable = back.interactable = codeInput.interactable = !on;
        }
    }
}
