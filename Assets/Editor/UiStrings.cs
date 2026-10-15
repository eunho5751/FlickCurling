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

            // ---- settings ----
            new[] { "settings.button", "설정", "Settings", "設定" },
            new[] { "settings.toMenu", "메뉴로 나가기", "Back to menu", "メニューに戻る" },
            new[] { "settings.soundOn", "소리 켬", "Sound on", "サウンド オン" },
            new[] { "settings.soundOff", "소리 끔", "Sound off", "サウンド オフ" },
            new[] { "settings.controls", "조작 방식", "Controls", "操作方法" },
            new[] { "controls.twoHand", "양손 조작", "Two-Hand", "両手操作" },
            new[] { "controls.oneHand", "한 손 조작", "One-Hand", "片手操作" },
            new[] { "settings.aimSens", "방향 감도", "Aim Sensitivity", "照準感度" },
            new[] { "settings.language", "언어", "Language", "言語" },
            new[] { "settings.speed", "{n}배속", "{n}× speed", "{n}倍速" },

            // ---- HUD ----
            new[] { "hud.hammer", "후공", "Last", "後攻" },
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
            new[] { "lsd.head", "표적에 가까운 쪽이 후공", "Closest to the target throws last", "的に近い方が後攻" },
            new[] { "lsd.body",
                "하우스 안에 매번 다른 자리에 표적이 생깁니다. 각자 알 하나를 표적에 최대한 가깝게 굴리고, 더 가까운 쪽이 1엔드의 후공(마지막 샷)이 됩니다. 최소 라인에 못 미치거나 백 라인을 넘으면 기록이 없습니다.",
                "A target appears somewhere new in the house each game. Each side draws one stone as close to it as possible; the closer one throws last in end 1. Stopping short of the min line or going past the back line leaves no record.",
                "ハウスの中に毎回違う位置に的が現れます。それぞれ石を1つ的のできるだけ近くに投げ、近い方が第1エンドの後攻（最後の1投）になります。最小ラインに届かない、またはバックラインを越えると記録なしです。" },
            new[] { "lsd.throwFirst", "{name}부터 굴리기", "{name} throws first", "{name}から投げる" },
            new[] { "lsd.resultEyebrow", "선후공 정하기 결과", "Turn order shot result", "先攻・後攻決めの結果" },
            new[] { "lsd.hasHammer", "{name} 후공", "{name} throws last", "{name}が後攻" },
            new[] { "lsd.coin", "두 기록이 같아 동전 던지기로 정했습니다.", "The two records were equal, so a coin toss decided it.", "記録が同じだったので、コイントスで決めました。" },
            new[] { "lsd.closer", "표적에 더 가까운 {name}이 1엔드의 마지막 샷을 가집니다.", "{name} was closer to the target and throws the last shot of end 1.", "的により近い{name}が第1エンドの最後の1投を持ちます。" },
            new[] { "lsd.shortOfHog", "최소 라인에 못 미쳤습니다.", "It stopped short of the min line.", "最小ラインに届きませんでした。" },
            new[] { "lsd.offSheet", "판 밖으로 나갔습니다.", "It went off the sheet.", "盤の外に出ました。" },

            // ---- ends ----
            new[] { "end.firstThrow", "{name}이 먼저 쏩니다.", "{name} throws first.", "{name}が先に投げます。" },
            new[] { "end.start", "{n}엔드 시작", "Start end {n}", "第{n}エンド開始" },
            new[] { "end.startExtra", "연장 엔드 시작", "Start the extra end", "延長エンド開始" },
            new[] { "end.final", "최종 결과 보기", "See the final result", "最終結果を見る" },
            new[] { "end.result", "{end} 결과", "{end} result", "{end}の結果" },
            new[] { "end.scored", "{name} 득점", "{name} scores", "{name}の得点" },
            new[] { "end.blank", "블랭크 엔드", "Blank end", "ブランクエンド" },
            new[] { "end.points", "{n}점", "{n:plural:{} point|{} points}", "{n}点" },
            new[] { "end.bodyScored", "다음 엔드 후공: {name} (점수를 내지 못한 쪽). 합계 {score}", "Next end, {name} throws last (the side that didn't score). Total {score}", "次のエンドの後攻：{name}（得点しなかった側）。合計 {score}" },
            new[] { "end.bodyBlank", "블랭크 엔드라 후공이 {name}에게 넘어갑니다. 합계 {score}", "Blank end, so {name} now throws last. Total {score}", "ブランクエンドなので、後攻は{name}に移ります。合計 {score}" },
            new[] { "score.versus", "{b} {bv} : {wv} {w}", "{b} {bv} : {wv} {w}", "{b} {bv} : {wv} {w}" },
            new[] { "bonus.title", "선공 보너스 가드", "First-thrower bonus guard", "先攻ボーナスガード" },
            new[] { "bonus.body", "{guard} 가드 1개 먼저 · {hammer} 마지막 샷", "{guard} starts with a guard · {hammer} has the last shot", "{guard}がガード1個から・{hammer}が最後の1投" },

            // ---- game over ----
            new[] { "over.eyebrow", "경기 종료", "Game over", "試合終了" },
            new[] { "over.win", "{name} 승리", "{name} wins", "{name}の勝ち" },
            new[] { "over.draw", "무승부", "Draw", "引き分け" },
            new[] { "over.drawBody", "연장 엔드까지 동점으로 끝났습니다.", "Still level after the extra end.", "延長エンドでも同点で終わりました。" },
            new[] { "over.again", "다시 하기", "Play again", "もう一度" },
            new[] { "over.watchAgain", "다시 관전", "Watch again", "もう一度観戦" },
            new[] { "over.menu", "메뉴로", "Menu", "メニューへ" },

            // ---- shot results ----
            new[] { "out.hog", "최소 라인을 넘지 못해 무효", "Short of the min line: no stone", "最小ラインを越えられず無効" },
            new[] { "out.back", "백 라인을 넘어가 무효", "Past the back line: no stone", "バックラインを越えて無効" },
            new[] { "out.knocked", "{team} {n}개 아웃", "{n} {team} out", "{team} {n}個アウト" },
            new[] { "foul.title", "쿠션권 없음", "No cushion ticket", "クッション券なし" },
            new[] { "foul.body",
                "이번 엔드의 쿠션권을 이미 썼습니다. 알을 되돌리고 방금 쏜 알을 제거합니다.",
                "Your cushion tickets for this end are used up. The stones go back and the stone just thrown is removed.",
                "このエンドのクッション券はもう使い切っています。石を元に戻し、今投げた石を取り除きます。" },

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
            new[] { "float.ticketUsed", "쿠션권 사용", "Ticket used", "クッション券使用" },
            new[] { "float.noTicket", "쿠션권 없음", "No ticket", "クッション券なし" },
            new[] { "float.cushion", "쿠션 {n}", "Cushion {n}", "クッション {n}" },
            new[] { "float.deadRail", "죽은 레일", "Dead rail", "デッドレール" },
            new[] { "board.sand", "모래", "Sand", "砂" },
            new[] { "board.ice", "빙판", "Ice", "氷" },
        };
    }
}
