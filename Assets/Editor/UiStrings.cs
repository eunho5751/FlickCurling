namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Source for the "UI" string table collection (ko is the project locale). LocalizationBuilder writes these into
    /// the Localization tables; entries with {placeholders} become Smart Strings.
    /// </summary>
    public static class UiStrings
    {
        public static readonly string[] Locales = { "ko", "en", "ja" };

        // key, ko, en, ja
        public static readonly string[][] Entries =
        {
            // ---- teams, levels, maps, modes ----
            new[] { "team.B", "흑", "Black", "黒" },
            new[] { "team.W", "백", "White", "白" },
            new[] { "team.ai", "(AI)", " (AI)", "(AI)" },
            new[] { "level.easy", "쉬움", "Easy", "やさしい" },
            new[] { "level.normal", "보통", "Normal", "ふつう" },
            new[] { "level.hard", "어려움", "Hard", "むずかしい" },
            new[] { "map.base", "기본", "Classic", "ベーシック" },
            new[] { "map.bumper", "범퍼", "Bumpers", "バンパー" },
            new[] { "map.zones", "모래·빙판", "Sand & Ice", "砂・氷" },
            new[] { "map.kink", "꺾인 측벽", "Bent Rails", "折れたサイドレール" },
            new[] { "mode.pvp", "2인 대전", "2 Players", "2人対戦" },
            new[] { "mode.vsAi", "AI {level}", "AI {level}", "AI {level}" },

            // ---- main menu scene ----
            new[] { "mainmenu.offline", "오프라인 대전", "Offline Match", "オフライン対戦" },
            new[] { "mainmenu.online", "온라인 대전", "Online Match", "オンライン対戦" },
            new[] { "mainmenu.leaderboard", "리더보드", "Leaderboard", "ランキング" },
            new[] { "mainmenu.rules", "게임 규칙", "How to Play", "ルール" },
            new[] { "mainmenu.shop", "상점", "Shop", "ショップ" },
            new[] { "mainmenu.settings", "설정", "Settings", "設定" },

            // ---- main menu: shop (test products for now) ----
            new[] { "shop.item.coins", "코인 한 줌", "Handful of Coins", "コインひとつかみ" },
            new[] { "shop.item.goldStone", "황금 알", "Golden Stone", "金の石" },
            new[] { "shop.item.chest", "보물 상자", "Treasure Chest", "宝箱" },

            // ---- main menu: match setup popup ----
            new[] { "matchsetup.mode", "모드", "Mode", "モード" },
            new[] { "matchsetup.vsAi", "AI", "AI", "AI" },
            new[] { "matchsetup.vsLocal", "로컬 2인", "Local 2P", "ローカル2人" },
            new[] { "matchsetup.map", "맵", "Map", "マップ" },
            new[] { "matchsetup.level", "난이도", "Difficulty", "難易度" },
            new[] { "matchsetup.ok", "확인", "OK", "OK" },

            // ---- online match ----
            new[] { "online.random", "랜덤 매칭", "Random Match", "ランダムマッチ" },
            new[] { "online.waiting", "상대를 기다리는 중…", "Waiting for opponent…", "相手を待っています…" },
            new[] { "online.opponentAiming", "상대가 조준하는 중…", "Opponent is aiming…", "相手が狙っています…" },
            new[] { "online.resuming", "경기로 돌아가는 중…", "Returning to the match…", "試合に戻っています…" },
            new[] { "online.connecting", "상대와 연결하는 중…", "Connecting to your opponent…", "対戦相手と接続しています…" },
            new[] { "online.forfeitTitle", "경기를 포기할까요?", "Give up the match?", "試合を棄権しますか？" },
            new[] { "online.forfeitBody", "지금 나가면 기권패로 기록돼요.", "Leaving now counts as a loss.", "今退出すると棄権負けになります。" },
            new[] { "online.forfeitLeave", "기권하고 나가기", "Forfeit and leave", "棄権して退出" },
            new[] { "online.forfeitStay", "계속하기", "Keep playing", "続ける" },
            new[] { "online.forfeitWin", "상대가 기권했어요", "Your opponent forfeited", "相手が棄権しました" },
            new[] { "online.forfeitGone", "상대 연결이 끊겨 기권승", "Opponent disconnected: you win", "相手の切断により不戦勝" },
            new[] { "online.forfeitLost", "연결이 끊겨 기권패 처리됐어요", "You were disconnected and lost by forfeit", "接続切れのため棄権負けになりました" },
            new[] { "online.noShow", "상대가 들어오지 않아 경기가 취소됐어요", "Your opponent didn't arrive, so the match was called off", "相手が来なかったため、試合は中止になりました" },
            new[] { "device.title", "접속 안내", "Signed In Elsewhere", "接続のお知らせ" },
            new[] { "network.title", "연결 안내", "No Connection", "接続のお知らせ" },
            new[] { "network.message", "네트워크 연결을 확인해 주세요.", "Please check your network connection.", "ネットワーク接続を確認してください。" },
            new[] { "device.message", "다른 기기에서 로그인했어요.\n이 기기에서 계속하려면 다시 접속해 주세요.", "Your account was signed in on another device.\nReconnect to keep playing here.", "別の端末でログインしました。\nこの端末で続けるには再接続してください。" },
            new[] { "device.reconnect", "다시 접속", "Reconnect", "再接続" },

            // ---- settings ----
            new[] { "settings.button", "설정", "Settings", "設定" },
            new[] { "settings.toMenu", "메인 메뉴로 이동", "Back to Main Menu", "メインメニューへ" },
            new[] { "settings.aimSens", "방향 감도", "Aim Sensitivity", "照準感度" },
            new[] { "settings.music", "배경음", "Music", "BGM" },
            new[] { "settings.sfx", "효과음", "Sound Effects", "効果音" },
            new[] { "settings.language", "언어", "Language", "言語" },

            // ---- settings: account (UGS) ----
            new[] { "account.pid", "PID", "PID", "PID" },
            new[] { "account.nickname", "닉네임", "Nickname", "ニックネーム" },
            new[] { "account.googleLogin", "Google로 로그인", "Sign in with Google", "Googleでログイン" },
            new[] { "account.googleLinked", "✓ Google로 연동됨", "✓ Linked with Google", "✓ Google連携済み" },
            new[] { "account.signingIn", "연결 중…", "Connecting…", "接続中…" },
            new[] { "account.offline", "서버에 연결하지 못했어요", "Couldn't reach the server", "サーバーに接続できませんでした" },
            new[] { "account.googleUnavailable", "이 기기에서는 Google 로그인을 쓸 수 없어요", "Google sign-in isn't available here", "この端末ではGoogleログインを使えません" },
            new[] { "account.linkFailed", "Google 연동에 실패했어요", "Couldn't link with Google", "Google連携に失敗しました" },

            // ---- account: linking an account that already has a record ----
            // ---- account: the nickname asked on the first start ----
            new[] { "nickname.title", "닉네임 설정", "Your nickname", "ニックネーム設定" },
            new[] { "nickname.message", "게임에서 사용할 닉네임을 정해 주세요.", "Choose the name you'll play under.", "ゲームで使うニックネームを決めてください。" },
            new[] { "nickname.placeholder", "닉네임 입력", "Nickname", "ニックネームを入力" },
            new[] { "nickname.guide", "3~12자, 한글·영문·숫자만 쓸 수 있어요\n(특수문자, 이모지, 공백 불가)", "3 to 12 characters, letters and numbers only\n(no symbols, emoji or spaces)", "3〜12文字、文字と数字のみ\n（記号・絵文字・スペース不可）" },
            new[] { "nickname.ok", "확인", "OK", "OK" },
            new[] { "nickname.failed", "닉네임을 저장하지 못했어요. 다시 시도해 주세요.", "Couldn't save the nickname. Please try again.", "ニックネームを保存できませんでした。もう一度お試しください。" },

            new[] { "link.title", "기록 선택", "Choose a record", "記録の選択" },
            new[] { "link.message", "이 Google 계정에는 이미 게임 기록이 있어요.\n어느 기록으로 플레이할까요?", "This Google account already has a game record.\nWhich one do you want to play with?", "このGoogleアカウントにはすでにゲーム記録があります。\nどちらの記録で遊びますか？" },
            new[] { "link.existing", "Google 계정의 기록", "The Google account's record", "Googleアカウントの記録" },
            new[] { "link.current", "지금 기록", "This record", "現在の記録" },
            new[] { "link.created", "시작한 날 {date}", "Started {date}", "開始日 {date}" },
            new[] { "link.load", "불러오기", "Load", "読み込む" },
            new[] { "link.keep", "덮어쓰기", "Overwrite", "上書き" },

            // ---- HUD ----
            new[] { "hud.hammer", "후공", "Last", "後攻" },
            new[] { "hud.first", "선공", "First", "先攻" },
            new[] { "hud.player", "플레이어", "Player", "プレイヤー" },
            new[] { "hud.player1", "플레이어 1", "Player 1", "プレイヤー1" },
            new[] { "hud.player2", "플레이어 2", "Player 2", "プレイヤー2" },
            new[] { "hud.ai", "AI", "AI", "AI" },
            new[] { "hud.record", "{w}승 {l}패 {d}무", "{w}W {l}L {d}D", "{w}勝 {l}敗 {d}分" },
            new[] { "hud.idle", "대기", "Idle", "待機" },
            new[] { "hud.end", "{n}엔드", "End {n}", "第{n}エンド" },
            new[] { "hud.extra", "연장", "Extra", "延長" },
            new[] { "hud.total", "합", "Total", "計" },
            new[] { "hud.turn", "{name} 차례", "{name} Turn", "{name}の番" },
            new[] { "hud.tickets", "쿠션권 {b} {bn} · {w} {wn}", "Tickets {b} {bn} · {w} {wn}", "クッション券 {b} {bn}・{w} {wn}" },
            new[] { "hud.noRecord", "기록 없음", "No record", "記録なし" },

            // ---- last stone draw ----
            new[] { "lsd.eyebrow", "{map} 맵 · {mode}", "{map} map · {mode}", "{map}マップ・{mode}" },
            new[] { "lsd.title", "선후공 정하기", "Turn Order Shot", "先攻・後攻決め" },
            new[] { "lsd.head", "하우스 중앙에 가까운 쪽이 1엔드의 후공이 됩니다!", "The one closer to the centre of the house throws last in end 1!", "ハウスの中心に近い方が1エンドの後攻になります！" },
            new[] { "lsd.start", "시작", "Start", "スタート" },
            new[] { "lsd.resultEyebrow", "선후공 정하기 결과", "Turn order shot result", "先攻・後攻決めの結果" },
            new[] { "lsd.hasHammer", "{first}<voffset=0.12em> <color=#d9a54c>선공</color>│</voffset>{name}<voffset=0.12em> <color=#d9a54c>후공</color></voffset>", "{first}<voffset=0.12em> <color=#d9a54c>First</color>│</voffset>{name}<voffset=0.12em> <color=#d9a54c>Last</color></voffset>", "{first}<voffset=0.12em><color=#d9a54c>先攻</color>│</voffset>{name}<voffset=0.12em><color=#d9a54c>後攻</color></voffset>" },
            new[] { "lsd.shortOfHog", "최소 라인에 못 미쳤습니다.", "It stopped short of the min line.", "最小ラインに届きませんでした。" },
            new[] { "lsd.offSheet", "판 밖으로 나갔습니다.", "It went off the sheet.", "盤の外に出ました。" },

            // ---- ends ----
            new[] { "end.start", "{n}엔드 시작", "Start end {n}", "第{n}エンド開始" },
            new[] { "end.startExtra", "연장 엔드 시작", "Start the extra end", "延長エンド開始" },
            new[] { "end.final", "최종 결과 보기", "See the final result", "最終結果を見る" },
            new[] { "end.result", "{end} 결과", "{end} result", "{end}の結果" },
            new[] { "end.scored", "{name} <color=#d9a54c>+{n}점 획득!</color> ({score})", "{name} <color=#d9a54c>scores {n}!</color> ({score})", "{name} <color=#d9a54c>{n}点獲得！</color>（{score}）" },
            new[] { "end.nextHammer", "다음 엔드 후공 : {name}", "Last shot next end: {name}", "次のエンドの後攻：{name}" },
            new[] { "end.blank", "<color=#d9a54c>블랭크 엔드</color> ({score})", "<color=#d9a54c>Blank end</color> ({score})", "<color=#d9a54c>ブランクエンド</color>（{score}）" },
            new[] { "score.versus", "{b} {bv} : {wv} {w}", "{b} {bv} : {wv} {w}", "{b} {bv} : {wv} {w}" },
            new[] { "bonus.title", "선공 보너스 가드", "First-thrower bonus guard", "先攻ボーナスガード" },

            // ---- game over ----
            new[] { "over.eyebrow", "경기 종료", "Game over", "試合終了" },
            new[] { "over.win", "{name} <color=#d9a54c>승리!</color> ({score})", "{name} <color=#d9a54c>wins!</color> ({score})", "{name}の<color=#d9a54c>勝ち！</color>（{score}）" },
            new[] { "over.draw", "<color=#d9a54c>무승부</color> ({score})", "<color=#d9a54c>Draw</color> ({score})", "<color=#d9a54c>引き分け</color>（{score}）" },
            new[] { "over.again", "다시 하기", "Play again", "もう一度" },
            new[] { "over.menu", "메인 메뉴로 이동", "Main Menu", "メインメニューへ" },
            new[] { "over.viewResult", "최종 결과 보기", "See the result", "最終結果を見る" },
            new[] { "over.waiting", "응답 기다리는 중…", "Waiting for the server…", "応答を待っています…" },
            new[] { "over.points", "{points}P ({delta})", "{points}P ({delta})", "{points}P（{delta}）" },
            new[] { "over.myWin", "<color=#d9a54c>승리!</color> ({score})", "<color=#d9a54c>Victory!</color> ({score})", "<color=#d9a54c>勝利！</color>（{score}）" },
            new[] { "over.myLoss", "패배! ({score})", "Defeat! ({score})", "敗北！（{score}）" },
            new[] { "over.myDraw", "<color=#d9a54c>무승부</color> ({score})", "<color=#d9a54c>Draw</color> ({score})", "<color=#d9a54c>引き分け</color>（{score}）" },
            new[] { "leaderboard.rank", "순위", "Rank", "順位" },
            new[] { "leaderboard.name", "닉네임", "Name", "名前" },
            new[] { "leaderboard.points", "승점", "Points", "勝ち点" },
            new[] { "leaderboard.winRate", "승률", "Win rate", "勝率" },
            new[] { "leaderboard.mine", "내 순위", "My rank", "自分の順位" },
            new[] { "leaderboard.unranked", "아직 순위가 없어요", "No rank yet", "まだ順位がありません" },
            new[] { "leaderboard.guest", "구글 로그인하면 순위에 올라가요", "Sign in with Google to get ranked", "Googleでログインするとランキングに載ります" },
            new[] { "leaderboard.empty", "아직 순위에 오른 플레이어가 없어요", "No one is ranked yet", "まだランキングに誰もいません" },
            new[] { "leaderboard.loading", "불러오는 중…", "Loading…", "読み込み中…" },
            new[] { "leaderboard.failed", "순위를 불러오지 못했어요", "Could not load the rankings", "ランキングを読み込めませんでした" },
            new[] { "out.hog", "최소 라인을 넘지 못해 무효", "Short of the min line: no stone", "最小ラインを越えられず無効" },
            new[] { "out.back", "아웃 라인을 넘어가 무효", "Past the out line: no stone", "アウトラインを越えて無効" },
            new[] { "out.near", "시트 아래로 나가 무효", "Off the near end: no stone", "手前に出て無効" },
            new[] { "out.knocked", "{team} {n}개 아웃", "{team} {n} out", "{team} {n}個アウト" },
            new[] { "timeout.title", "시간 초과", "Time's up", "時間切れ" },
            new[] { "timeout.body", "{name} 알을 잃었습니다", "{name} loses this stone", "{name}はこの石を失いました" },
            new[] { "timeout.lsd", "기록 없이 넘어갑니다", "No record for this draw", "記録なしで次へ" },
            new[] { "foul.lost",
                "이번 엔드는 더이상 쿠션이 불가능합니다.\n{team} 알을 잃었습니다.",
                "No more cushions this end.\n{team} loses this stone.",
                "このエンドはもうクッションできません。\n{team}の石を失いました。" },

            // ---- on the sheet ----
            new[] { "float.ticketMinus", "쿠션권 -1", "Cushion ticket -1", "クッション券 -1" },
            new[] { "float.noTicket", "쿠션권 없음", "No ticket", "クッション券なし" },
            new[] { "board.sand", "모래", "Sand", "砂" },
            new[] { "board.ice", "빙판", "Ice", "氷" },
        };
    }
}
