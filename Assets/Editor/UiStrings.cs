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
            new[] { "map.random", "랜덤", "Random", "ランダム" },
            new[] { "mode.pvp", "2인 대전", "2 Players", "2人対戦" },
            new[] { "mode.easy", "AI 쉬움", "AI Easy", "AI やさしい" },
            new[] { "mode.normal", "AI 보통", "AI Normal", "AI ふつう" },
            new[] { "mode.hard", "AI 어려움", "AI Hard", "AI むずかしい" },
            new[] { "mode.watch", "AI 관전", "Watch AI", "AI観戦" },
            new[] { "mode.vsAi", "AI {level}", "AI {level}", "AI {level}" },
            new[] { "mode.watchVs", "AI 관전 · {b} {lb} vs {w} {lw}", "Watch AI · {b} {lb} vs {w} {lw}", "AI観戦・{b} {lb} vs {w} {lw}" },

            // ---- start menu ----
            new[] { "menu.eyebrow", "Unity 이식판 · 룰과 손맛 검증용", "Unity port · testing the rules and feel", "Unity移植版・ルールと手触りの検証用" },
            new[] { "menu.title", "알까기 컬링", "Flick Curling", "おはじきカーリング" },
            new[] { "menu.lede",
                "바둑알을 튕겨 상대 알은 쳐내고 내 알만 중앙에 남기세요. 4엔드, 엔드당 10샷(1인당 알 5개)이며 중앙에 가장 가까운 쪽만 득점합니다.",
                "Flick go stones to knock your opponent's out and leave only yours in the middle. Four ends of 10 shots (5 stones each); only the side closest to the centre scores.",
                "碁石をはじいて相手の石を弾き出し、自分の石だけを中央に残しましょう。4エンド、1エンド10投（1人5個）で、中心に最も近い側だけが得点します。" },
            new[] { "menu.note",
                "최소 라인을 넘지 못하거나 백 라인을 넘어간 알은 무효입니다. 측벽 쿠션은 허용됩니다.",
                "A stone that stops short of the min line or goes past the back line doesn't count. Banking off the side rails is allowed.",
                "最小ラインを越えられなかった石と、バックラインを越えた石は無効です。サイドレールのクッションは使えます。" },
            new[] { "menu.mode", "모드", "MODE", "モード" },
            new[] { "menu.watchLevels", "관전할 AI 난이도", "AI LEVELS TO WATCH", "観戦するAIの強さ" },
            new[] { "menu.map", "맵", "MAP", "マップ" },
            new[] { "menu.start", "경기 시작", "Start", "試合開始" },

            // ---- main menu scene ----
            new[] { "mainmenu.offline", "오프라인 대전", "Offline Match", "オフライン対戦" },
            new[] { "mainmenu.online", "온라인 대전", "Online Match", "オンライン対戦" },
            new[] { "mainmenu.leaderboard", "리더보드", "Leaderboard", "ランキング" },
            new[] { "mainmenu.rules", "게임 규칙", "How to Play", "ルール" },
            new[] { "mainmenu.settings", "설정", "Settings", "設定" },

            // ---- main menu: match setup popup ----
            new[] { "matchsetup.opponent", "상대", "Opponent", "対戦相手" },
            new[] { "matchsetup.vsAi", "AI", "AI", "AI" },
            new[] { "matchsetup.vsLocal", "로컬 2인", "Local 2P", "ローカル2人" },
            new[] { "matchsetup.mode", "모드", "Mode", "モード" },
            new[] { "matchsetup.modeBasic", "기본", "Classic", "ベーシック" },
            new[] { "matchsetup.level", "난이도", "Difficulty", "難易度" },
            new[] { "matchsetup.ok", "확인", "OK", "OK" },

            // ---- settings ----
            new[] { "settings.button", "설정", "Settings", "設定" },
            new[] { "settings.toMenu", "메뉴로 나가기", "Back to menu", "メニューに戻る" },
            new[] { "settings.soundOn", "소리 켬", "Sound on", "サウンド オン" },
            new[] { "settings.soundOff", "소리 끔", "Sound off", "サウンド オフ" },
            new[] { "settings.aimSens", "방향 감도", "Aim Sensitivity", "照準感度" },
            new[] { "settings.music", "배경음", "Music", "BGM" },
            new[] { "settings.sfx", "효과음", "Sound Effects", "効果音" },
            new[] { "settings.language", "언어", "Language", "言語" },
            new[] { "settings.speed", "{n}배속", "{n}× speed", "{n}倍速" },

            // ---- HUD ----
            new[] { "hud.hammer", "후공", "Last", "後攻" },
            new[] { "hud.first", "선공", "First", "先攻" },
            new[] { "hud.idle", "대기", "Idle", "待機" },
            new[] { "hud.end", "{n}엔드", "End {n}", "第{n}エンド" },
            new[] { "hud.extra", "연장", "Extra", "延長" },
            new[] { "hud.total", "합", "Total", "計" },
            new[] { "hud.turn", "{name} 차례", "{name} Turn", "{name}の番" },
            new[] { "hud.endOver", "엔드 종료", "End over", "エンド終了" },
            new[] { "hud.lsd", "선후공 정하기", "Turn Order Shot", "先攻・後攻決め" },
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
            new[] { "over.watchAgain", "다시 관전", "Watch again", "もう一度観戦" },
            new[] { "over.menu", "메인 메뉴로 이동", "Main Menu", "メインメニューへ" },
            new[] { "out.hog", "최소 라인을 넘지 못해 무효", "Short of the min line: no stone", "最小ラインを越えられず無効" },
            new[] { "out.back", "아웃 라인을 넘어가 무효", "Past the out line: no stone", "アウトラインを越えて無効" },
            new[] { "out.knocked", "{team} {n}개 아웃", "{team} {n} out", "{team} {n}個アウト" },
            new[] { "timeout.title", "시간 초과", "Time's up", "時間切れ" },
            new[] { "timeout.body", "{name} 알을 잃었습니다", "{name} loses this stone", "{name}はこの石を失いました" },
            new[] { "timeout.lsd", "기록 없이 넘어갑니다", "No record for this draw", "記録なしで次へ" },
            new[] { "foul.lost",
                "이번 엔드는 더이상 쿠션이 불가능합니다.\n{team} 알을 잃었습니다.",
                "No more cushions this end.\n{team} loses this stone.",
                "このエンドはもうクッションできません。\n{team}の石を失いました。" },

            // ---- on the sheet ----
            new[] { "ai.thinking", "{name} 생각 중…", "{name} is thinking…", "{name} 考え中…" },
            new[] { "ai.pulling", "{name} · {intent}", "{name} · {intent}", "{name}・{intent}" },
            new[] { "intent.draw", "드로우", "draw", "ドロー" },
            new[] { "intent.guard", "가드", "guard", "ガード" },
            new[] { "intent.takeout", "테이크아웃", "takeout", "テイクアウト" },
            new[] { "intent.hit-stay", "히트 앤 스테이", "hit and stay", "ヒット＆ステイ" },
            new[] { "intent.bank", "쿠션", "bank shot", "クッション" },
            new[] { "intent.bank-takeout", "쿠션 테이크아웃", "bank takeout", "クッションテイクアウト" },
            new[] { "intent.blank", "블랭크", "blank", "ブランク" },
            new[] { "intent.lsd", "표적 맞히기", "target throw", "ターゲットショット" },
            new[] { "float.ticketMinus", "쿠션권 -1", "Cushion ticket -1", "クッション券 -1" },
            new[] { "float.noTicket", "쿠션권 없음", "No ticket", "クッション券なし" },
            new[] { "board.sand", "모래", "Sand", "砂" },
            new[] { "board.ice", "빙판", "Ice", "氷" },
        };
    }
}
