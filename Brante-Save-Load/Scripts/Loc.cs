using System.Collections.Generic;
using I2.Loc;

namespace Doorstop
{
	// The mod's own UI strings, in the languages the game ships (English,
	// Russian, German, French, Spanish, Italian, Polish, Turkish, Japanese,
	// Korean, Simplified Chinese). The game has no localisation keys for
	// these, so they live here; ChoiceWindow rebuilds itself when the game
	// language changes, which re-reads them.
	public static class Loc
	{
		public enum K
		{
			Title, SaveNow, History, Playthrough,
			Load, Delete, Save, Cancel,
			ConfirmLoad, ConfirmDelete, ConfirmSave, EmptySaves,
			ColType, ColSaved, ColChapter, ColAge, ColYear, ColSeason, ColTurn, ColScene, ColChoice,
			TypeAuto, TypeManual, Current, NothingYet,
			KindAll, KindAutosave, KindCheckpoint, KindOriginal, ShowMore,
			LoadScene, Upcoming, NotRecorded, Requires, OneOf, NotWord, OrWord, CopyPathId, PastePathId, PathIdCopied, PathIdBad, PathIdLoading, PidEmpty, PidShape, PidLength, PidCheck, PidVersion, PidDamaged, NoSaveBefore, PickBlocked, ResetPath, PickNext, OnThisScene, ReplayLoading, ReplayProgress, ReplayLeft, ReplayStop, UnitSec, UnitMin,
			PickConflict, NothingToLoad,
			PossibleOn, OnStats, OnRelations, OnFates, OnEvents, AndWord, NoStatText, MissingPick, ReplayOffPath, ReplayDeath, ReplayStopped,
			ReplaySkipped, StartNotOnPath,
			HelpAutosave, HelpCheckpoint, HelpManual, HelpOriginal,
			EventHappens,
			StatsTab,
			StoryEnds, TheEnd, FinaleEnds, OutcomeNote, UnlocksIn, DestEarned, DestAhead, DestOpen, DestLost, DestEarnedBy, ChapterWord,
			
		}

		static Loc()
		{
			AddLoadSceneWord(_en, "Load scene");
			AddSaveKinds(_en, "All", "Game autosave", "Before load", "Original save", "Show more");
			AddPathChecks(_en,
				"Load scene stopped at \"{0}\": the story skipped \"{1}\" from your path, so its answer could not be used.",
				"The save that would start this path belongs to another timeline. Pick an earlier scene, or load a save of this path.");
			AddKindHelp(_en,
				"The game's own autosave, copied as the game writes it. Load it to continue from where the game last saved.",
				"A snapshot taken automatically before a load or a chapter restart. Use it to undo that rewind.",
				"A save you made with Save now.",
				"The one-time copy of your game save from before the mod was first used. It is read-only.");
			AddLoadSceneWord(_ru, "Загрузить сцену");
			AddSaveKinds(_ru, "Все", "Автосохранение игры", "Перед загрузкой", "Исходное сохранение", "Показать ещё");
			AddPathChecks(_ru,
				"Загрузка остановилась на «{0}»: история пропустила «{1}» с вашего пути, и его ответ не использован.",
				"Сохранение, с которого начинался бы этот путь, относится к другой линии. Выберите более раннюю сцену или загрузите сохранение этого пути.");
			AddKindHelp(_ru,
				"Автосохранение самой игры, скопированное при записи. Загрузите, чтобы продолжить с последнего сохранения игры.",
				"Снимок, сделанный автоматически перед загрузкой или перезапуском главы. Вернёт состояние до этого отката.",
				"Сохранение, сделанное вами кнопкой «Сохранить».",
				"Одноразовая копия сохранения игры до первого использования мода. Только для чтения.");
			AddLoadSceneWord(_de, "Szene laden");
			AddSaveKinds(_de, "Alle", "Spiel-Autosave", "Vor dem Laden", "Originalspeicher", "Mehr anzeigen");
			AddPathChecks(_de,
				"Laden hielt bei „{0}“: die Geschichte übersprang „{1}“ auf deinem Pfad, seine Antwort konnte nicht genutzt werden.",
				"Der Spielstand, von dem dieser Pfad starten würde, gehört zu einer anderen Zeitlinie. Wähle eine frühere Szene oder lade einen Spielstand dieses Pfads.");
			AddKindHelp(_de,
				"Der Autosave des Spiels, kopiert beim Speichern. Laden, um dort weiterzumachen, wo das Spiel zuletzt gespeichert hat.",
				"Ein automatischer Schnappschuss vor dem Laden oder einem Kapitelneustart. Macht die Rückspulung rückgängig.",
				"Ein Spielstand, den du mit „Jetzt speichern“ angelegt hast.",
				"Die einmalige Kopie deines Spielstands von vor der ersten Nutzung des Mods. Schreibgeschützt.");
			AddLoadSceneWord(_fr, "Charger la scène");
			AddSaveKinds(_fr, "Tout", "Sauvegarde auto", "Avant chargement", "Sauvegarde d'origine", "Afficher plus");
			AddPathChecks(_fr,
				"Le chargement s'est arrêté à « {0} » : l'histoire a sauté « {1} » de votre chemin, sa réponse n'a pas pu être utilisée.",
				"La sauvegarde d'où partirait ce chemin appartient à une autre chronologie. Choisissez une scène plus tôt ou chargez une sauvegarde de ce chemin.");
			AddKindHelp(_fr,
				"La sauvegarde automatique du jeu, copiée à chaque écriture. Chargez-la pour reprendre où le jeu s'est arrêté.",
				"Un instantané pris automatiquement avant un chargement ou un redémarrage de chapitre. Annule ce retour en arrière.",
				"Une sauvegarde faite avec « Enregistrer ».",
				"La copie unique de votre sauvegarde d'origine, prise avant la première utilisation du mod. En lecture seule.");
			AddLoadSceneWord(_es, "Cargar escena");
			AddSaveKinds(_es, "Todo", "Autoguardado", "Antes de cargar", "Guardado original", "Mostrar más");
			AddPathChecks(_es,
				"La carga se detuvo en «{0}»: la historia saltó «{1}» de tu camino, así que su respuesta no se pudo usar.",
				"La partida desde la que empezaría este camino es de otra línea temporal. Elige una escena anterior o carga una partida de este camino.");
			AddKindHelp(_es,
				"El autoguardado del juego, copiado tal como lo escribe. Cárgalo para seguir donde el juego guardó por última vez.",
				"Una instantánea tomada automáticamente antes de cargar o reiniciar un capítulo. Deshace ese retroceso.",
				"Una partida guardada con «Guardar ahora».",
				"La copia única de tu partida original, hecha antes de usar el mod por primera vez. Solo lectura.");
			AddLoadSceneWord(_it, "Carica scena");
			AddSaveKinds(_it, "Tutto", "Salvataggio auto", "Prima del caricamento", "Salvataggio originale", "Mostra altro");
			AddPathChecks(_it,
				"Il caricamento si è fermato a «{0}»: la storia ha saltato «{1}» dal tuo percorso, quindi la sua risposta non è stata usata.",
				"Il salvataggio da cui partirebbe questo percorso appartiene a un'altra linea temporale. Scegli una scena precedente o carica un salvataggio di questo percorso.");
			AddKindHelp(_it,
				"Il salvataggio automatico del gioco, copiato quando scrive. Caricalo per riprendere dall'ultimo salvataggio del gioco.",
				"Un'istantanea presa automaticamente prima di un caricamento o di un riavvio di capitolo. Annulla quel ritorno indietro.",
				"Un salvataggio fatto con «Salva ora».",
				"La copia unica del tuo salvataggio originale, fatta prima del primo uso della mod. Sola lettura.");
			AddLoadSceneWord(_pl, "Wczytaj scenę");
			AddSaveKinds(_pl, "Wszystkie", "Autozapis gry", "Przed wczytaniem", "Oryginalny zapis", "Pokaż więcej");
			AddPathChecks(_pl,
				"Wczytywanie zatrzymało się na „{0}”: historia pominęła „{1}” na twojej ścieżce, więc jego odpowiedź nie została użyta.",
				"Zapis, od którego zaczęłaby się ta ścieżka, należy do innej linii czasu. Wybierz wcześniejszą scenę albo wczytaj zapis tej ścieżki.");
			AddKindHelp(_pl,
				"Autozapis gry, kopiowany przy każdym zapisie. Wczytaj, aby kontynuować od ostatniego zapisu gry.",
				"Migawka wykonana automatycznie przed wczytaniem lub restartem rozdziału. Cofa to cofnięcie.",
				"Zapis wykonany przyciskiem „Zapisz teraz”.",
				"Jednorazowa kopia twojego zapisu sprzed pierwszego użycia moda. Tylko do odczytu.");
			AddLoadSceneWord(_tr, "Sahneyi yükle");
			AddSaveKinds(_tr, "Tümü", "Oyun otomatik kaydı", "Yüklemeden önce", "Özgün kayıt", "Daha fazla");
			AddPathChecks(_tr,
				"Yükleme \"{0}\" sahnesinde durdu: hikâye yolundaki \"{1}\" sahnesini atladı, bu yüzden cevabı kullanılamadı.",
				"Bu yolun başlayacağı kayıt başka bir zaman çizgisine ait. Daha önceki bir sahneyi seç ya da bu yolun bir kaydını yükle.");
			AddKindHelp(_tr,
				"Oyunun kendi otomatik kaydı, yazıldıkça kopyalanır. Oyunun son kaydettiği yerden devam etmek için yükle.",
				"Bir yükleme veya bölüm yeniden başlatmadan önce otomatik alınan anlık görüntü. O geri sarmayı geri alır.",
				"«Şimdi kaydet» ile yaptığın kayıt.",
				"Modun ilk kullanımından önceki oyun kaydının tek seferlik kopyası. Salt okunur.");
			AddLoadSceneWord(_ja, "シーンを読み込む");
			AddSaveKinds(_ja, "すべて", "ゲームの自動保存", "読み込み前", "元のセーブ", "さらに表示");
			AddPathChecks(_ja,
				"「{0}」で読み込みが止まりました：物語が道筋の「{1}」を飛ばしたため、その回答は使われませんでした。",
				"この道筋の開始に使うセーブは別の時間軸のものです。もっと前のシーンを選ぶか、この道筋のセーブを読み込んでください。");
			AddKindHelp(_ja,
				"ゲーム自身の自動保存を書き込み時にコピーしたもの。ゲームが最後に保存した場所から続けるには読み込んでください。",
				"読み込みや章の再開始の前に自動で取るスナップショット。その巻き戻しを取り消せます。",
				"「今すぐ保存」で作ったセーブ。",
				"モッドを初めて使う前のゲームのセーブの一度きりのコピー。読み取り専用。");
			AddLoadSceneWord(_ko, "장면 불러오기");
			AddSaveKinds(_ko, "전체", "게임 자동 저장", "불러오기 전", "원본 저장", "더 보기");
			AddPathChecks(_ko,
				"\"{0}\"에서 불러오기가 멈췄습니다: 이야기가 경로의 \"{1}\"을(를) 건너뛰어 그 답이 사용되지 않았습니다.",
				"이 경로를 시작할 저장은 다른 시간대의 것입니다. 더 이전 장면을 고르거나 이 경로의 저장을 불러오세요.");
			AddKindHelp(_ko,
				"게임이 저장할 때 복사한 게임 자체의 자동 저장입니다. 게임이 마지막으로 저장한 곳에서 계속하려면 불러오세요.",
				"불러오기나 챕터 다시 시작 전에 자동으로 만든 스냅샷입니다. 그 되돌리기를 취소합니다.",
				"«지금 저장»으로 만든 저장입니다.",
				"모드를 처음 쓰기 전 게임 저장의 한 번뿐인 복사본입니다. 읽기 전용.");
			AddLoadSceneWord(_zh, "读取场景");
			AddSaveKinds(_zh, "全部", "游戏自动保存", "读取前", "原始存档", "显示更多");
			AddPathChecks(_zh,
				"读取在“{0}”停止：剧情跳过了路径上的“{1}”，因此未能使用该回答。",
				"用于开始这条路径的存档属于另一条时间线。请选择更早的场景，或读取这条路径的存档。");
			AddKindHelp(_zh,
				"游戏自身写入的自动保存副本。读取它可从游戏上次保存处继续。",
				"在读取或重开章节前自动拍摄的快照。可撤销那次回退。",
				"用“立即保存”创建的存档。",
				"首次使用模组前游戏存档的一次性副本，只读。");
			AddRequirementWords(_en, "Requires", "One of", "not");
			AddRequirementWords(_ru, "Требуется", "Одно из", "не");
			AddRequirementWords(_de, "Erfordert", "Eines von", "nicht");
			AddRequirementWords(_fr, "Requiert", "Au choix", "pas");
			AddRequirementWords(_es, "Requiere", "Uno de", "no");
			AddRequirementWords(_it, "Richiede", "Uno tra", "non");
			AddRequirementWords(_pl, "Wymaga", "Jedno z", "nie");
			AddRequirementWords(_tr, "Gerekir", "Şunlardan biri", "değil");
			AddRequirementWords(_ja, "必要条件", "いずれか", "ではない");
			AddRequirementWords(_ko, "필요 조건", "다음 중 하나", "아님");
			AddRequirementWords(_zh, "需要", "其中之一", "非");
			AddResetPath(_en, "Reset path");
			AddResetPath(_ru, "Сбросить путь");
			AddResetPath(_de, "Pfad zurücksetzen");
			AddResetPath(_fr, "Réinitialiser le chemin");
			AddResetPath(_es, "Restablecer el camino");
			AddResetPath(_it, "Ripristina il percorso");
			AddResetPath(_pl, "Resetuj ścieżkę");
			AddResetPath(_tr, "Yolu sıfırla");
			AddResetPath(_ja, "道筋をリセット");
			AddResetPath(_ko, "경로 초기화");
			AddResetPath(_zh, "重置路径");
			_en[K.EventHappens] = "Happens on its own";
			_ru[K.EventHappens] = "Происходит само собой";
			_de[K.EventHappens] = "Geschieht von selbst";
			_fr[K.EventHappens] = "Arrive de lui-même";
			_es[K.EventHappens] = "Ocurre por sí solo";
			_it[K.EventHappens] = "Accade da sé";
			_pl[K.EventHappens] = "Dzieje się samo";
			_tr[K.EventHappens] = "Kendiliğinden olur";
			_ja[K.EventHappens] = "選択なしで起こる";
			_ko[K.EventHappens] = "저절로 일어남";
			_zh[K.EventHappens] = "自然发生";
			_en[K.StoryEnds] = "The story ends here: after a true death no choices follow, only the chapter's finale.";
			_ru[K.StoryEnds] = "Здесь история заканчивается: после истинной смерти выборов больше нет, только финал главы.";
			_de[K.StoryEnds] = "Hier endet die Geschichte: nach dem wahren Tod folgen keine Entscheidungen mehr, nur das Finale des Kapitels.";
			_fr[K.StoryEnds] = "L'histoire s'arrête ici : après la vraie mort, plus aucun choix, seulement le dénouement du chapitre.";
			_es[K.StoryEnds] = "La historia termina aquí: tras la muerte verdadera no hay más decisiones, solo el final del capítulo.";
			_it[K.StoryEnds] = "La storia finisce qui: dopo la vera morte non ci sono più scelte, solo il finale del capitolo.";
			_pl[K.StoryEnds] = "Tu historia się kończy: po prawdziwej śmierci nie ma już wyborów, tylko finał rozdziału.";
			_tr[K.StoryEnds] = "Hikâye burada biter: gerçek ölümden sonra seçim yoktur, yalnızca bölümün finali.";
			_ja[K.StoryEnds] = "物語はここで終わる。真の死の後に選択肢はなく、章の結末だけが続く。";
			_ko[K.StoryEnds] = "이야기는 여기서 끝납니다. 진정한 죽음 이후에는 선택지가 없고 장의 결말만 이어집니다.";
			_zh[K.StoryEnds] = "故事到此结束：实亡之后不再有选择，只有本章的结局。";
			_en[K.DestEarned] = "Happened";
			_ru[K.DestEarned] = "Произошло";
			_de[K.DestEarned] = "Geschehen";
			_fr[K.DestEarned] = "Arrivé";
			_es[K.DestEarned] = "Ocurrido";
			_it[K.DestEarned] = "Avvenuto";
			_pl[K.DestEarned] = "Wydarzyło się";
			_tr[K.DestEarned] = "Gerçekleşti";
			_ja[K.DestEarned] = "達成";
			_ko[K.DestEarned] = "달성";
			_zh[K.DestEarned] = "已发生";
			_en[K.DestAhead] = "On this path";
			_ru[K.DestAhead] = "На этом пути";
			_de[K.DestAhead] = "Auf diesem Pfad";
			_fr[K.DestAhead] = "Sur ce chemin";
			_es[K.DestAhead] = "En este camino";
			_it[K.DestAhead] = "Su questo percorso";
			_pl[K.DestAhead] = "Na tej ścieżce";
			_tr[K.DestAhead] = "Bu yolda";
			_ja[K.DestAhead] = "この道で";
			_ko[K.DestAhead] = "이 경로에서";
			_zh[K.DestAhead] = "此路线上";
			_en[K.DestOpen] = "Still possible";
			_ru[K.DestOpen] = "Ещё возможно";
			_de[K.DestOpen] = "Noch möglich";
			_fr[K.DestOpen] = "Encore possible";
			_es[K.DestOpen] = "Aún posible";
			_it[K.DestOpen] = "Ancora possibile";
			_pl[K.DestOpen] = "Wciąż możliwe";
			_tr[K.DestOpen] = "Hâlâ mümkün";
			_ja[K.DestOpen] = "まだ可能";
			_ko[K.DestOpen] = "아직 가능";
			_zh[K.DestOpen] = "仍有可能";
			_en[K.DestLost] = "No longer possible";
			_ru[K.DestLost] = "Уже невозможно";
			_de[K.DestLost] = "Nicht mehr möglich";
			_fr[K.DestLost] = "Plus possible";
			_es[K.DestLost] = "Ya no es posible";
			_it[K.DestLost] = "Non più possibile";
			_pl[K.DestLost] = "Już niemożliwe";
			_tr[K.DestLost] = "Artık mümkün değil";
			_ja[K.DestLost] = "もう不可能";
			_ko[K.DestLost] = "더 이상 불가능";
			_zh[K.DestLost] = "已不可能";
			_en[K.DestEarnedBy] = "Comes from";
			_ru[K.DestEarnedBy] = "Получается в";
			_de[K.DestEarnedBy] = "Kommt aus";
			_fr[K.DestEarnedBy] = "Vient de";
			_es[K.DestEarnedBy] = "Viene de";
			_it[K.DestEarnedBy] = "Viene da";
			_pl[K.DestEarnedBy] = "Pochodzi z";
			_tr[K.DestEarnedBy] = "Kaynağı";
			_ja[K.DestEarnedBy] = "発生元";
			_ko[K.DestEarnedBy] = "발생 장면";
			_zh[K.DestEarnedBy] = "来自";
			_en[K.OrWord] = "or";
			_ru[K.OrWord] = "или";
			_de[K.OrWord] = "oder";
			_fr[K.OrWord] = "ou";
			_es[K.OrWord] = "o";
			_it[K.OrWord] = "o";
			_pl[K.OrWord] = "lub";
			_tr[K.OrWord] = "veya";
			_ja[K.OrWord] = "または";
			_ko[K.OrWord] = "또는";
			_zh[K.OrWord] = "或";
			_en[K.CopyPathId] = "Copy path ID: the scene on screen and everything about it, as text to paste into another game";
			_en[K.PastePathId] = "Paste path ID: load the scene and setup a copied path ID holds (this game is kept as a “Before load” save)";
			_en[K.PathIdCopied] = "Path ID copied ({0} characters).";
			_en[K.PathIdBad] = "The clipboard holds no valid path ID: {0}.";
			_en[K.PathIdLoading] = "Loading the pasted path ID…";
			_ru[K.CopyPathId] = "Копировать ID пути: сцена на экране и всё о ней, текстом для вставки в другую игру";
			_ru[K.PastePathId] = "Вставить ID пути: загрузить сцену и состояние из скопированного ID (эта игра сохранится как «Перед загрузкой»)";
			_ru[K.PathIdCopied] = "ID пути скопирован ({0} символов).";
			_ru[K.PathIdBad] = "В буфере обмена нет правильного ID пути: {0}.";
			_ru[K.PathIdLoading] = "Загрузка вставленного ID пути…";
			_de[K.CopyPathId] = "Pfad-ID kopieren: die Szene auf dem Bildschirm und alles dazu, als Text für ein anderes Spiel";
			_de[K.PastePathId] = "Pfad-ID einfügen: Szene und Stand einer kopierten Pfad-ID laden (dieses Spiel bleibt als „Vor dem Laden“ erhalten)";
			_de[K.PathIdCopied] = "Pfad-ID kopiert ({0} Zeichen).";
			_de[K.PathIdBad] = "Die Zwischenablage enthält keine gültige Pfad-ID: {0}.";
			_de[K.PathIdLoading] = "Eingefügte Pfad-ID wird geladen…";
			_fr[K.CopyPathId] = "Copier l’ID de parcours : la scène à l’écran et tout son état, en texte à coller dans une autre partie";
			_fr[K.PastePathId] = "Coller un ID de parcours : charger la scène et l’état qu’il contient (cette partie est gardée en sauvegarde « Avant chargement »)";
			_fr[K.PathIdCopied] = "ID de parcours copié ({0} caractères).";
			_fr[K.PathIdBad] = "Le presse-papiers ne contient pas d’ID de parcours valide : {0}.";
			_fr[K.PathIdLoading] = "Chargement de l’ID de parcours collé…";
			_es[K.CopyPathId] = "Copiar ID de ruta: la escena en pantalla y todo su estado, como texto para pegar en otra partida";
			_es[K.PastePathId] = "Pegar ID de ruta: cargar la escena y el estado que contiene (esta partida se guarda como «Antes de cargar»)";
			_es[K.PathIdCopied] = "ID de ruta copiado ({0} caracteres).";
			_es[K.PathIdBad] = "El portapapeles no contiene un ID de ruta válido: {0}.";
			_es[K.PathIdLoading] = "Cargando el ID de ruta pegado…";
			_it[K.CopyPathId] = "Copia ID percorso: la scena sullo schermo e tutto il suo stato, come testo da incollare in un’altra partita";
			_it[K.PastePathId] = "Incolla ID percorso: carica la scena e lo stato che contiene (questa partita resta come salvataggio «Prima del caricamento»)";
			_it[K.PathIdCopied] = "ID percorso copiato ({0} caratteri).";
			_it[K.PathIdBad] = "Gli appunti non contengono un ID percorso valido: {0}.";
			_it[K.PathIdLoading] = "Caricamento dell’ID percorso incollato…";
			_pl[K.CopyPathId] = "Kopiuj ID ścieżki: scena na ekranie i cały jej stan, jako tekst do wklejenia w innej grze";
			_pl[K.PastePathId] = "Wklej ID ścieżki: wczytaj scenę i stan z skopiowanego ID (ta gra zostanie zapisana jako „Przed wczytaniem”)";
			_pl[K.PathIdCopied] = "Skopiowano ID ścieżki ({0} znaków).";
			_pl[K.PathIdBad] = "Schowek nie zawiera poprawnego ID ścieżki: {0}.";
			_pl[K.PathIdLoading] = "Wczytywanie wklejonego ID ścieżki…";
			_tr[K.CopyPathId] = "Yol kimliğini kopyala: ekrandaki sahne ve tüm durumu, başka bir oyuna yapıştırılacak metin olarak";
			_tr[K.PastePathId] = "Yol kimliğini yapıştır: kopyalanan kimliğin sahnesini ve durumunu yükle (bu oyun \"Yüklemeden önce\" kaydı olarak kalır)";
			_tr[K.PathIdCopied] = "Yol kimliği kopyalandı ({0} karakter).";
			_tr[K.PathIdBad] = "Panoda geçerli bir yol kimliği yok: {0}.";
			_tr[K.PathIdLoading] = "Yapıştırılan yol kimliği yükleniyor…";
			_ja[K.CopyPathId] = "パスIDをコピー：画面上のシーンとその状態すべてを、別のゲームに貼り付けられるテキストとして";
			_ja[K.PastePathId] = "パスIDを貼り付け：コピーしたIDのシーンと状態を読み込む（このゲームは「読み込み前」セーブとして残ります）";
			_ja[K.PathIdCopied] = "パスIDをコピーしました（{0}文字）。";
			_ja[K.PathIdBad] = "クリップボードに有効なパスIDがありません：{0}。";
			_ja[K.PathIdLoading] = "貼り付けたパスIDを読み込み中…";
			_ko[K.CopyPathId] = "경로 ID 복사: 화면의 장면과 그 상태 전체를 다른 게임에 붙여 넣을 텍스트로";
			_ko[K.PastePathId] = "경로 ID 붙여넣기: 복사한 ID의 장면과 상태를 불러오기 (이 게임은 \"불러오기 전\" 저장으로 남습니다)";
			_ko[K.PathIdCopied] = "경로 ID를 복사했습니다 ({0}자).";
			_ko[K.PathIdBad] = "클립보드에 올바른 경로 ID가 없습니다: {0}.";
			_ko[K.PathIdLoading] = "붙여 넣은 경로 ID를 불러오는 중…";
			_zh[K.CopyPathId] = "复制路径 ID：屏幕上的场景及其全部状态，作为可粘贴到另一局游戏的文本";
			_zh[K.PastePathId] = "粘贴路径 ID：读取所复制 ID 中的场景和状态（本局会保存为“读取前”存档）";
			_zh[K.PathIdCopied] = "已复制路径 ID（{0} 个字符）。";
			_zh[K.PathIdBad] = "剪贴板中没有有效的路径 ID：{0}。";
			_zh[K.PathIdLoading] = "正在读取粘贴的路径 ID…";
			_en[K.ChapterWord] = "Chapter";
			_ru[K.ChapterWord] = "Глава";
			_de[K.ChapterWord] = "Kapitel";
			_fr[K.ChapterWord] = "Chapitre";
			_es[K.ChapterWord] = "Capítulo";
			_it[K.ChapterWord] = "Capitolo";
			_pl[K.ChapterWord] = "Rozdział";
			_tr[K.ChapterWord] = "Bölüm";
			_ja[K.ChapterWord] = "章";
			_ko[K.ChapterWord] = "장";
			_zh[K.ChapterWord] = "章";
			_en[K.UnlocksIn] = "Unlocks in Chapter {0}";
			_ru[K.UnlocksIn] = "Откроется в главе {0}";
			_de[K.UnlocksIn] = "Ab Kapitel {0}";
			_fr[K.UnlocksIn] = "Débloqué au chapitre {0}";
			_es[K.UnlocksIn] = "Se desbloquea en el capítulo {0}";
			_it[K.UnlocksIn] = "Si sblocca nel capitolo {0}";
			_pl[K.UnlocksIn] = "Odblokowane w rozdziale {0}";
			_tr[K.UnlocksIn] = "Bölüm {0}'de açılır";
			_ja[K.UnlocksIn] = "第{0}章で解放";
			_ko[K.UnlocksIn] = "{0}장에서 해금";
			_zh[K.UnlocksIn] = "第{0}章解锁";
			_en[K.TheEnd] = "The End";
			_ru[K.TheEnd] = "Конец";
			_de[K.TheEnd] = "Das Ende";
			_fr[K.TheEnd] = "Fin";
			_es[K.TheEnd] = "Fin";
			_it[K.TheEnd] = "Fine";
			_pl[K.TheEnd] = "Koniec";
			_tr[K.TheEnd] = "Son";
			_ja[K.TheEnd] = "終わり";
			_ko[K.TheEnd] = "끝";
			_zh[K.TheEnd] = "终";
			_en[K.FinaleEnds] = "The story ends here: the game moves on to its finale and epilogue, and no choices follow.";
			_en[K.OutcomeNote] = "The game then tells what became of the Revolt, and the epilogue follows. No choices are left.";
			_ru[K.FinaleEnds] = "Здесь история заканчивается: игра переходит к финалу и эпилогу, выборов больше нет.";
			_ru[K.OutcomeNote] = "Затем игра рассказывает, чем закончилось восстание, и следует эпилог. Выборов больше нет.";
			_de[K.FinaleEnds] = "Hier endet die Geschichte: das Spiel geht zu Finale und Epilog über, es folgen keine Entscheidungen mehr.";
			_de[K.OutcomeNote] = "Danach erzählt das Spiel, wie der Aufstand ausging, und der Epilog folgt. Es gibt keine Entscheidungen mehr.";
			_fr[K.FinaleEnds] = "L'histoire s'arrête ici : le jeu passe au dénouement et à l'épilogue, sans autre choix.";
			_fr[K.OutcomeNote] = "Le jeu raconte ensuite l'issue de la révolte, puis vient l'épilogue. Il ne reste aucun choix.";
			_es[K.FinaleEnds] = "La historia termina aquí: el juego pasa al final y al epílogo, sin más decisiones.";
			_es[K.OutcomeNote] = "Después el juego cuenta cómo terminó la revuelta y sigue el epílogo. No quedan decisiones.";
			_it[K.FinaleEnds] = "La storia finisce qui: il gioco passa al finale e all'epilogo, senza altre scelte.";
			_it[K.OutcomeNote] = "Poi il gioco racconta come è finita la rivolta e segue l'epilogo. Non restano scelte.";
			_pl[K.FinaleEnds] = "Tu historia się kończy: gra przechodzi do finału i epilogu, wyborów już nie ma.";
			_pl[K.OutcomeNote] = "Potem gra opowiada, jak skończyła się rewolta, i następuje epilog. Nie ma już wyborów.";
			_tr[K.FinaleEnds] = "Hikâye burada biter: oyun finale ve sonsöze geçer, başka seçim yoktur.";
			_tr[K.OutcomeNote] = "Ardından oyun isyanın nasıl sonuçlandığını anlatır ve sonsöz gelir. Başka seçim kalmaz.";
			_ja[K.FinaleEnds] = "物語はここで終わる。ゲームは結末とエピローグへ進み、選択肢はもうない。";
			_ja[K.OutcomeNote] = "その後、反乱の結末が語られ、エピローグが続く。選択肢はもうない。";
			_ko[K.FinaleEnds] = "이야기는 여기서 끝납니다. 게임은 결말과 에필로그로 넘어가며 더 이상 선택지가 없습니다.";
			_ko[K.OutcomeNote] = "그 후 게임은 반란이 어떻게 끝났는지 보여 주고 에필로그가 이어집니다. 더 이상 선택지가 없습니다.";
			_zh[K.FinaleEnds] = "故事到此结束：游戏进入结局与尾声，不再有选择。";
			_zh[K.OutcomeNote] = "随后游戏讲述起义的结局，接着是尾声。不再有选择。";
			_en[K.StatsTab] = "Stats";
			_ru[K.StatsTab] = "Характеристики";
			_de[K.StatsTab] = "Werte";
			_fr[K.StatsTab] = "Caractéristiques";
			_es[K.StatsTab] = "Atributos";
			_it[K.StatsTab] = "Statistiche";
			_pl[K.StatsTab] = "Statystyki";
			_tr[K.StatsTab] = "İstatistikler";
			_ja[K.StatsTab] = "能力値";
			_ko[K.StatsTab] = "능력치";
			_zh[K.StatsTab] = "属性";
			AddPickNext(_en, "Pick an answer here to go on");
			AddPickNext(_ru, "Выберите здесь ответ, чтобы продолжить");
			AddPickNext(_de, "Wähle hier eine Antwort, um weiterzugehen");
			AddPickNext(_fr, "Choisissez une réponse ici pour continuer");
			AddPickNext(_es, "Elige aquí una respuesta para continuar");
			AddPickNext(_it, "Scegli qui una risposta per proseguire");
			AddPickNext(_pl, "Wybierz tutaj odpowiedź, aby iść dalej");
			AddPickNext(_tr, "Devam etmek için burada bir cevap seç");
			AddPickNext(_ja, "先へ進むにはここで答えを選択");
			AddPickNext(_ko, "계속하려면 여기서 답을 고르세요");
			AddPickNext(_zh, "在此选择一个回答以继续");
			AddOnThisScene(_en, "You are on this scene now: give this answer in the game, or pick answers in later scenes to load further.");
			AddOnThisScene(_ru, "Вы сейчас в этой сцене: дайте этот ответ в игре или выберите ответы в следующих сценах, чтобы загрузить дальше.");
			AddOnThisScene(_de, "Du bist gerade in dieser Szene: Gib diese Antwort im Spiel oder wähle Antworten in späteren Szenen, um weiter zu laden.");
			AddOnThisScene(_fr, "Vous êtes dans cette scène : donnez cette réponse dans le jeu, ou choisissez des réponses dans les scènes suivantes pour charger plus loin.");
			AddOnThisScene(_es, "Estás en esta escena: da esta respuesta en el juego o elige respuestas en escenas posteriores para cargar más adelante.");
			AddOnThisScene(_it, "Sei in questa scena: dai questa risposta nel gioco, o scegli risposte nelle scene successive per caricare più avanti.");
			AddOnThisScene(_pl, "Jesteś teraz w tej scenie: udziel tej odpowiedzi w grze albo wybierz odpowiedzi w dalszych scenach, aby wczytać dalej.");
			AddOnThisScene(_tr, "Şu an bu sahnedesin: bu cevabı oyunda ver ya da daha ileri yüklemek için sonraki sahnelerde cevap seç.");
			AddOnThisScene(_ja, "今このシーンにいます。ゲーム内でこの答えを選ぶか、後のシーンで答えを選んでさらに先をロードしてください。");
			AddOnThisScene(_ko, "지금 이 장면에 있습니다. 게임에서 이 답을 고르거나, 더 나아가려면 이후 장면에서 답을 고르세요.");
			AddOnThisScene(_zh, "你正处于这个场景：请在游戏中给出这个回答，或在之后的场景中选择回答以加载更远处。");
			AddReplayTexts(_en, "Loading \u201c{0}\u201d", "Scene {0} of {1}");
			AddReplayTexts(_ru, "Загрузка \u00ab{0}\u00bb", "Сцена {0} из {1}");
			AddReplayTexts(_de, "Lade \u201e{0}\u201c", "Szene {0} von {1}");
			AddReplayTexts(_fr, "Chargement de \u00ab {0} \u00bb", "Scène {0} sur {1}");
			AddReplayTexts(_es, "Cargando \u00ab{0}\u00bb", "Escena {0} de {1}");
			AddReplayTexts(_it, "Caricamento di \u00ab{0}\u00bb", "Scena {0} di {1}");
			AddReplayTexts(_pl, "Wczytywanie \u201e{0}\u201d", "Scena {0} z {1}");
			AddReplayTexts(_tr, "\u201c{0}\u201d yükleniyor", "Sahne {0} / {1}");
			AddReplayTexts(_ja, "\u300c{0}\u300dを読み込み中", "シーン {0} / {1}");
			AddReplayTexts(_ko, "\u201c{0}\u201d 불러오는 중", "장면 {0} / {1}");
			AddReplayTexts(_zh, "正在加载\u201c{0}\u201d", "场景 {0} / {1}");
			AddReplayTime(_en, "{0} left", "Stop", "s", "min");
			AddReplayTime(_ru, "осталось {0}", "Стоп", "с", "мин");
			AddReplayTime(_de, "noch {0}", "Stopp", "s", "min");
			AddReplayTime(_fr, "{0} restantes", "Arrêter", "s", "min");
			AddReplayTime(_es, "quedan {0}", "Detener", "s", "min");
			AddReplayTime(_it, "{0} rimanenti", "Ferma", "s", "min");
			AddReplayTime(_pl, "pozostało {0}", "Zatrzymaj", "s", "min");
			AddReplayTime(_tr, "{0} kaldı", "Durdur", "sn", "dk");
			AddReplayTime(_ja, "残り {0}", "停止", "秒", "分");
			AddReplayTime(_ko, "{0} 남음", "중지", "초", "분");
			AddReplayTime(_zh, "剩余 {0}", "停止", "秒", "分");
			AddPossibleOn(_en, "Possible depending on {0}", "stats", "relationships", "characters' fates", "story events", "and");
			AddPossibleOn(_ru, "Возможно в зависимости от: {0}", "характеристик", "отношений", "судеб персонажей", "событий сюжета", "и");
			AddPossibleOn(_de, "Möglich, je nach {0}", "Werten", "Beziehungen", "Schicksalen der Figuren", "Ereignissen der Geschichte", "und");
			AddPossibleOn(_fr, "Possible selon {0}", "les caractéristiques", "les relations", "le destin des personnages", "les événements de l'histoire", "et");
			AddPossibleOn(_es, "Posible según {0}", "las características", "las relaciones", "el destino de los personajes", "los sucesos de la historia", "y");
			AddPossibleOn(_it, "Possibile a seconda di {0}", "caratteristiche", "rapporti", "destino dei personaggi", "eventi della storia", "e");
			AddPossibleOn(_pl, "Możliwe, zależnie od: {0}", "statystyk", "relacji", "losów postaci", "wydarzeń fabuły", "i");
			AddPossibleOn(_tr, "Şunlara bağlı olarak mümkün: {0}", "değerler", "ilişkiler", "karakterlerin kaderi", "hikâye olayları", "ve");
			AddPossibleOn(_ja, "{0}次第で起こりうる", "能力値", "関係", "登場人物の運命", "物語の出来事", "と");
			AddPossibleOn(_ko, "{0}에 따라 가능", "능력치", "관계", "인물의 운명", "이야기 사건", "및");
			AddPossibleOn(_zh, "可能出现，取决于{0}", "属性", "关系", "角色命运", "剧情事件", "和");
			AddNoStatText(_en, "The game has no description for this stat.");
			AddNoStatText(_ru, "В игре нет описания этой характеристики.");
			AddNoStatText(_de, "Das Spiel hat für diesen Wert keine Beschreibung.");
			AddNoStatText(_fr, "Le jeu ne donne aucune description de cette caractéristique.");
			AddNoStatText(_es, "El juego no tiene descripción para esta característica.");
			AddNoStatText(_it, "Il gioco non ha una descrizione per questa caratteristica.");
			AddNoStatText(_pl, "Gra nie ma opisu tej statystyki.");
			AddNoStatText(_tr, "Oyunda bu değer için bir açıklama yok.");
			AddNoStatText(_ja, "この能力値にはゲーム内の説明がありません。");
			AddNoStatText(_ko, "게임에 이 능력치에 대한 설명이 없습니다.");
			AddNoStatText(_zh, "游戏中没有这项属性的说明。");
			AddMissingPick(_en, "Pick an answer in every scene before the one you load: \"{0}\" has none yet.");
			AddMissingPick(_ru, "Выберите ответ в каждой сцене до загружаемой: в «{0}» его пока нет.");
			AddMissingPick(_de, "Wähle in jeder Szene vor der geladenen eine Antwort: „{0}“ hat noch keine.");
			AddMissingPick(_fr, "Choisissez une réponse dans chaque scène avant celle à charger : « {0} » n'en a pas encore.");
			AddMissingPick(_es, "Elige una respuesta en cada escena antes de la que cargas: «{0}» aún no tiene.");
			AddMissingPick(_it, "Scegli una risposta in ogni scena prima di quella da caricare: «{0}» non ne ha ancora.");
			AddMissingPick(_pl, "Wybierz odpowiedź w każdej scenie przed wczytywaną: „{0}” jeszcze jej nie ma.");
			AddMissingPick(_tr, "Yükleyeceğin sahneden önceki her sahnede bir cevap seç: \"{0}\" için henüz yok.");
			AddMissingPick(_ja, "読み込むシーンより前のすべてのシーンで回答を選んでください：「{0}」はまだ未選択です。");
			AddMissingPick(_ko, "불러올 장면 이전의 모든 장면에서 답을 고르세요: \"{0}\"에는 아직 없습니다.");
			AddMissingPick(_zh, "请在要读取的场景之前的每个场景中选择答案：“{0}”尚未选择。");
			AddReplayStops(_en, "Load scene stopped at \"{0}\": the story went another way and this scene is not on your path. Pick its answer here and load again.", "Load scene stopped at a death in \"{0}\" that no answer on your path explains, or the hero's last one. The choice is yours.", "Load scene stopped at \"{0}\" before reaching \"{1}\".");
			AddReplayStops(_ru, "Загрузка остановилась на «{0}»: история пошла иначе, и этой сцены нет на вашем пути. Выберите здесь ответ и загрузите снова.", "Загрузка остановилась на смерти в «{0}», которую не объясняет ни один ответ на пути, или на последней смерти героя. Выбор за вами.", "Загрузка остановилась на «{0}», не дойдя до «{1}».");
			AddReplayStops(_de, "Laden hielt bei „{0}“: die Geschichte nahm einen anderen Weg, diese Szene liegt nicht auf deinem Pfad. Wähle hier ihre Antwort und lade erneut.", "Laden hielt bei einem Tod in „{0}“, den keine Antwort auf deinem Pfad erklärt, oder beim letzten Tod des Helden. Die Wahl liegt bei dir.", "Laden hielt bei „{0}“, bevor „{1}“ erreicht war.");
			AddReplayStops(_fr, "Le chargement s'est arrêté à « {0} » : l'histoire a pris un autre chemin et cette scène n'est pas sur le vôtre. Choisissez sa réponse ici et rechargez.", "Le chargement s'est arrêté sur une mort dans « {0} » qu'aucune réponse de votre chemin n'explique, ou sur la dernière du héros. Le choix vous revient.", "Le chargement s'est arrêté à « {0} » avant d'atteindre « {1} ».");
			AddReplayStops(_es, "La carga se detuvo en «{0}»: la historia tomó otro camino y esta escena no está en el tuyo. Elige aquí su respuesta y carga de nuevo.", "La carga se detuvo en una muerte en «{0}» que ninguna respuesta de tu camino explica, o en la última del héroe. La elección es tuya.", "La carga se detuvo en «{0}» antes de llegar a «{1}».");
			AddReplayStops(_it, "Il caricamento si è fermato a «{0}»: la storia ha preso un'altra strada e questa scena non è sul tuo percorso. Scegli qui la sua risposta e carica di nuovo.", "Il caricamento si è fermato a una morte in «{0}» che nessuna risposta del percorso spiega, o all'ultima dell'eroe. La scelta è tua.", "Il caricamento si è fermato a «{0}» prima di raggiungere «{1}».");
			AddReplayStops(_pl, "Wczytywanie zatrzymało się na „{0}”: historia potoczyła się inaczej i tej sceny nie ma na twojej ścieżce. Wybierz tu odpowiedź i wczytaj ponownie.", "Wczytywanie zatrzymało się na śmierci w „{0}”, której nie wyjaśnia żadna odpowiedź na ścieżce, albo na ostatniej śmierci bohatera. Wybór należy do ciebie.", "Wczytywanie zatrzymało się na „{0}”, zanim dotarło do „{1}”.");
			AddReplayStops(_tr, "Yükleme \"{0}\" sahnesinde durdu: hikâye başka bir yöne gitti ve bu sahne yolunda değil. Cevabını burada seç ve yeniden yükle.", "Yükleme \"{0}\" sahnesinde, yolundaki hiçbir cevabın açıklamadığı bir ölümde ya da kahramanın son ölümünde durdu. Seçim senin.", "Yükleme \"{1}\" sahnesine varmadan \"{0}\" sahnesinde durdu.");
			AddReplayStops(_ja, "「{0}」で読み込みが止まりました：物語が別の道に進み、このシーンは道筋上にありません。ここで回答を選び、もう一度読み込んでください。", "「{0}」で、道筋上のどの回答にも当てはまらない死、または主人公の最後の死があり、読み込みが止まりました。選択はあなた次第です。", "「{1}」に着く前に「{0}」で読み込みが止まりました。");
			AddReplayStops(_ko, "\"{0}\"에서 불러오기가 멈췄습니다: 이야기가 다른 길로 갔고 이 장면은 경로에 없습니다. 여기서 답을 고르고 다시 불러오세요.", "\"{0}\"에서 경로의 어떤 답으로도 설명되지 않는 죽음, 또는 주인공의 마지막 죽음으로 불러오기가 멈췄습니다. 선택은 당신의 몫입니다.", "\"{1}\"에 닿기 전에 \"{0}\"에서 불러오기가 멈췄습니다.");
			AddReplayStops(_zh, "读取在“{0}”停止：剧情走向了另一条路，这个场景不在你的路径上。请在这里选择答案后再次读取。", "读取在“{0}”的一次死亡处停止：路径上没有答案能解释这次死亡，或这是主角的最后一次死亡。由你来选择。", "读取在到达“{1}”之前于“{0}”停止。");
			AddConflict(_en, "The answer in \"{0}\" on your path conflicts with your hero's stats at that moment: an earlier pick changed them. Pick another answer there.", "Nothing to load: choices selected correspond to the current scene.");
			AddConflict(_ru, "Ответ в «{0}» на вашем пути противоречит характеристикам героя в тот момент: их изменил более ранний выбор. Выберите там другой ответ.", "Загружать нечего: выбранные ответы соответствуют текущей сцене.");
			AddConflict(_de, "Die Antwort in „{0}“ auf deinem Weg passt nicht zu den Werten deines Helden in diesem Moment: eine frühere Wahl hat sie verändert. Wähle dort eine andere Antwort.", "Nichts zu laden: die gewählten Antworten entsprechen der aktuellen Szene.");
			AddConflict(_fr, "La réponse dans « {0} » sur votre chemin n'est pas compatible avec les caractéristiques de votre héros à ce moment-là : un choix antérieur les a modifiées. Choisissez-y une autre réponse.", "Rien à charger : les choix sélectionnés correspondent à la scène actuelle.");
			AddConflict(_es, "La respuesta en «{0}» de tu camino choca con las características de tu héroe en ese momento: una elección anterior las cambió. Elige otra respuesta allí.", "Nada que cargar: las elecciones seleccionadas corresponden a la escena actual.");
			AddConflict(_it, "La risposta in «{0}» sul tuo percorso è in conflitto con le caratteristiche del tuo eroe in quel momento: una scelta precedente le ha cambiate. Scegli lì un'altra risposta.", "Niente da caricare: le scelte selezionate corrispondono alla scena attuale.");
			AddConflict(_pl, "Odpowiedź w „{0}” na twojej ścieżce koliduje ze statystykami bohatera w tym momencie: zmienił je wcześniejszy wybór. Wybierz tam inną odpowiedź.", "Nie ma czego wczytać: wybrane odpowiedzi odpowiadają bieżącej scenie.");
			AddConflict(_tr, "Yolundaki \"{0}\" cevabı, kahramanının o andaki değerleriyle çelişiyor: daha önceki bir seçim onları değiştirdi. Orada başka bir cevap seç.", "Yüklenecek bir şey yok: seçilen cevaplar mevcut sahneye karşılık geliyor.");
			AddConflict(_ja, "道筋上の「{0}」の回答は、その時点の主人公の能力値と矛盾しています（それ以前の選択で変わりました）。そこで別の回答を選んでください。", "読み込むものはありません：選んだ回答は現在のシーンと同じです。");
			AddConflict(_ko, "경로에 있는 \"{0}\"의 답이 그 시점 주인공의 능력치와 맞지 않습니다(앞선 선택이 능력치를 바꿨습니다). 그곳에서 다른 답을 고르세요.", "불러올 것이 없습니다: 선택한 답이 현재 장면과 같습니다.");
			AddConflict(_zh, "你路径上“{0}”中的答案与主角当时的属性冲突：之前的选择改变了属性。请在那里选择其他答案。", "无需读取：所选答案与当前场景一致。");
			AddPickBlocked(_en, "The answer picked in \"{0}\" would not be open to your hero there.");
			AddPickBlocked(_ru, "Ответ, выбранный в «{0}», там будет недоступен вашему герою.");
			AddPickBlocked(_de, "Die in „{0}“ gewählte Antwort stünde deinem Helden dort nicht offen.");
			AddPickBlocked(_fr, "La réponse choisie dans « {0} » ne serait pas accessible à votre héros.");
			AddPickBlocked(_es, "La respuesta elegida en «{0}» no estaría disponible para tu héroe allí.");
			AddPickBlocked(_it, "La risposta scelta in «{0}» non sarebbe disponibile per il tuo eroe.");
			AddPickBlocked(_pl, "Odpowiedź wybrana w „{0}” nie byłaby tam dostępna dla twojego bohatera.");
			AddPickBlocked(_tr, "\"{0}\" içinde seçilen cevap kahramanına orada açık olmazdı.");
			AddPickBlocked(_ja, "「{0}」で選んだ回答は、そこではあなたの主人公には選べません。");
			AddPickBlocked(_ko, "\"{0}\"에서 고른 답은 그 시점의 주인공이 선택할 수 없습니다.");
			AddPickBlocked(_zh, "在“{0}”中选择的答案届时对你的主角不可用。");
			AddNotRecorded(_en, "Played but answer not recorded\n(mod was not installed)", "The mod has no save from before this point, so it cannot be reached.");
			AddNotRecorded(_ru, "Пройдено, но ответ не записан\n(мод не был установлен)", "У мода нет сохранения до этого места, поэтому сюда не попасть.");
			AddNotRecorded(_de, "Gespielt, aber Antwort nicht aufgezeichnet\n(Mod war nicht installiert)", "Der Mod hat keinen Spielstand vor dieser Stelle, sie ist nicht erreichbar.");
			AddNotRecorded(_fr, "Joué, mais réponse non enregistrée\n(le mod n'était pas installé)", "Le mod n'a aucune sauvegarde avant ce point : impossible d'y aller.");
			AddNotRecorded(_es, "Jugado, pero respuesta no registrada\n(el mod no estaba instalado)", "El mod no tiene ninguna partida anterior a este punto; no se puede llegar.");
			AddNotRecorded(_it, "Giocato, ma risposta non registrata\n(la mod non era installata)", "La mod non ha salvataggi prima di questo punto: non è raggiungibile.");
			AddNotRecorded(_pl, "Rozegrane, ale odpowiedź niezapisana\n(mod nie był zainstalowany)", "Mod nie ma zapisu sprzed tego miejsca, więc nie da się tu dotrzeć.");
			AddNotRecorded(_tr, "Oynandı ama cevap kaydedilmedi\n(mod yüklü değildi)", "Modun bu noktadan önceye ait kaydı yok, buraya ulaşılamaz.");
			AddNotRecorded(_ja, "プレイ済みだが回答は未記録\n（MOD未導入）", "この地点より前のセーブがないため、ここへは移動できません。");
			AddNotRecorded(_ko, "플레이했지만 답변이 기록되지 않음\n(모드 미설치)", "이 지점 이전의 저장이 없어 이동할 수 없습니다.");
			AddNotRecorded(_zh, "已经历，但未记录选择\n（当时未安装模组）", "此处之前没有模组存档，无法前往。");
			AddUpcomingWord(_en, "upcoming");
			AddUpcomingWord(_ru, "впереди");
			AddUpcomingWord(_de, "kommt noch");
			AddUpcomingWord(_fr, "à venir");
			AddUpcomingWord(_es, "por venir");
			AddUpcomingWord(_it, "in arrivo");
			AddUpcomingWord(_pl, "przed tobą");
			AddUpcomingWord(_tr, "sırada");
			AddUpcomingWord(_ja, "この先");
			AddUpcomingWord(_ko, "앞으로");
			AddUpcomingWord(_zh, "接下来");
			// Brazilian Portuguese and Traditional Chinese: the game's other two
			// text languages (its two Spanish ones share _es). True Death is
			// the game's own "Morte Genuína" / "真正的死亡".
			AddLoadSceneWord(_pt, "Carregar cena");
			AddSaveKinds(_pt, "Todos", "Salvamento automático do jogo", "Antes de carregar", "Save original", "Mostrar mais");
			AddPathChecks(_pt, "Carregar cena parou em \"{0}\": a história pulou \"{1}\" do seu caminho, então a resposta dela não pôde ser usada.", "O save que iniciaria este caminho pertence a outra linha do tempo. Escolha uma cena anterior ou carregue um save deste caminho.");
			AddKindHelp(_pt, "O salvamento automático do próprio jogo, copiado quando o jogo o grava. Carregue-o para continuar de onde o jogo salvou por último.", "Um instantâneo feito automaticamente antes de carregar ou reiniciar um capítulo. Use-o para desfazer esse retrocesso.", "Um save que você fez com Salvar agora.", "A cópia única do save do seu jogo de antes do primeiro uso do mod. Somente leitura.");
			AddRequirementWords(_pt, "Requer", "Um de", "não");
			AddResetPath(_pt, "Redefinir caminho");
			_pt[K.EventHappens] = "Acontece por si só";
			_pt[K.StoryEnds] = "A história termina aqui: depois da Morte Genuína não há mais escolhas, só o final do capítulo.";
			_pt[K.DestEarned] = "Aconteceu";
			_pt[K.DestAhead] = "Neste caminho";
			_pt[K.DestOpen] = "Ainda possível";
			_pt[K.DestLost] = "Não é mais possível";
			_pt[K.DestEarnedBy] = "Vem de";
			_pt[K.OrWord] = "ou";
			_pt[K.CopyPathId] = "Copiar ID do caminho: a cena na tela e tudo sobre ela, como texto para colar em outro jogo";
			_pt[K.PastePathId] = "Colar ID do caminho: carregar a cena e o estado que um ID copiado contém (este jogo fica guardado como save “Antes de carregar”)";
			_pt[K.PathIdCopied] = "ID do caminho copiado ({0} caracteres).";
			_pt[K.PathIdBad] = "A área de transferência não contém um ID de caminho válido: {0}.";
			_pt[K.PathIdLoading] = "Carregando o ID de caminho colado…";
			_pt[K.ChapterWord] = "Capítulo";
			_pt[K.UnlocksIn] = "Desbloqueia no Capítulo {0}";
			_pt[K.TheEnd] = "Fim";
			_pt[K.FinaleEnds] = "A história termina aqui: o jogo segue para o final e o epílogo, e não há mais escolhas.";
			_pt[K.OutcomeNote] = "Depois o jogo conta como terminou a revolta, e segue-se o epílogo. Não restam escolhas.";
			_pt[K.StatsTab] = "Atributos";
			AddPickNext(_pt, "Escolha uma resposta aqui para continuar");
			AddOnThisScene(_pt, "Você está nesta cena agora: dê esta resposta no jogo ou escolha respostas em cenas posteriores para carregar mais adiante.");
			AddReplayTexts(_pt, "Carregando “{0}”", "Cena {0} de {1}");
			AddReplayTime(_pt, "{0} restantes", "Parar", "s", "min");
			AddPossibleOn(_pt, "Possível dependendo de {0}", "atributos", "relacionamentos", "destinos dos personagens", "eventos da história", "e");
			AddNoStatText(_pt, "O jogo não tem descrição para este atributo.");
			AddMissingPick(_pt, "Escolha uma resposta em todas as cenas antes da que você carrega: \"{0}\" ainda não tem.");
			AddReplayStops(_pt, "Carregar cena parou em \"{0}\": a história seguiu outro rumo e esta cena não está no seu caminho. Escolha a resposta dela aqui e carregue de novo.", "Carregar cena parou em uma morte em \"{0}\" que nenhuma resposta do seu caminho explica, ou na última do herói. A escolha é sua.", "Carregar cena parou em \"{0}\" antes de chegar a \"{1}\".");
			AddConflict(_pt, "A resposta em \"{0}\" no seu caminho entra em conflito com os atributos do herói naquele momento: uma escolha anterior os mudou. Escolha outra resposta ali.", "Nada para carregar: as escolhas selecionadas correspondem à cena atual.");
			AddPickBlocked(_pt, "A resposta escolhida em \"{0}\" não estaria disponível para o seu herói ali.");
			AddNotRecorded(_pt, "Jogado, mas a resposta não foi gravada\n(o mod não estava instalado)", "O mod não tem um save de antes deste ponto, então não é possível chegar aqui.");
			AddUpcomingWord(_pt, "a seguir");
			AddLoadSceneWord(_tw, "載入場景");
			AddSaveKinds(_tw, "全部", "遊戲自動存檔", "載入前", "原始存檔", "顯示更多");
			AddPathChecks(_tw, "載入在「{0}」停止：劇情跳過了路徑上的「{1}」，因此未能使用該回答。", "用於開始這條路徑的存檔屬於另一條時間線。請選擇更早的場景，或載入這條路徑的存檔。");
			AddKindHelp(_tw, "遊戲自身寫入的自動存檔副本。載入它可從遊戲上次存檔處繼續。", "在載入或重開章節前自動建立的快照。可撤銷那次回退。", "用「立即存檔」建立的存檔。", "首次使用模組前遊戲存檔的一次性副本，唯讀。");
			AddRequirementWords(_tw, "需要", "其中之一", "非");
			AddResetPath(_tw, "重設路徑");
			_tw[K.EventHappens] = "自然發生";
			_tw[K.StoryEnds] = "故事到此結束：真正的死亡之後不再有選擇，只有本章的結局。";
			_tw[K.DestEarned] = "已發生";
			_tw[K.DestAhead] = "此路線上";
			_tw[K.DestOpen] = "仍有可能";
			_tw[K.DestLost] = "已不可能";
			_tw[K.DestEarnedBy] = "來自";
			_tw[K.OrWord] = "或";
			_tw[K.CopyPathId] = "複製路徑 ID：畫面上的場景及其全部狀態，作為可貼到另一局遊戲的文字";
			_tw[K.PastePathId] = "貼上路徑 ID：載入所複製 ID 中的場景和狀態（本局會保存為「載入前」存檔）";
			_tw[K.PathIdCopied] = "已複製路徑 ID（{0} 個字元）。";
			_tw[K.PathIdBad] = "剪貼簿中沒有有效的路徑 ID：{0}。";
			_tw[K.PathIdLoading] = "正在載入貼上的路徑 ID…";
			_tw[K.ChapterWord] = "章";
			_tw[K.UnlocksIn] = "第{0}章解鎖";
			_tw[K.TheEnd] = "終";
			_tw[K.FinaleEnds] = "故事到此結束：遊戲進入結局與尾聲，不再有選擇。";
			_tw[K.OutcomeNote] = "隨後遊戲講述起義的結局，接著是尾聲。不再有選擇。";
			_tw[K.StatsTab] = "屬性";
			AddPickNext(_tw, "在此選擇一個回答以繼續");
			AddOnThisScene(_tw, "你正處於這個場景：請在遊戲中給出這個回答，或在之後的場景中選擇回答以載入更遠處。");
			AddReplayTexts(_tw, "正在載入「{0}」", "場景 {0} / {1}");
			AddReplayTime(_tw, "剩餘 {0}", "停止", "秒", "分");
			AddPossibleOn(_tw, "可能出現，取決於{0}", "屬性", "關係", "角色命運", "劇情事件", "和");
			AddNoStatText(_tw, "遊戲中沒有這項屬性的說明。");
			AddMissingPick(_tw, "請在要載入的場景之前的每個場景中選擇答案：「{0}」尚未選擇。");
			AddReplayStops(_tw, "載入在「{0}」停止：劇情走向了另一條路，這個場景不在你的路徑上。請在這裡選擇答案後再次載入。", "載入在「{0}」的一次死亡處停止：路徑上沒有答案能解釋這次死亡，或這是主角的最後一次死亡。由你來選擇。", "載入在到達「{1}」之前於「{0}」停止。");
			AddConflict(_tw, "你路徑上「{0}」中的答案與主角當時的屬性衝突：之前的選擇改變了屬性。請在那裡選擇其他答案。", "無需載入：所選答案與目前場景一致。");
			AddPickBlocked(_tw, "在「{0}」中選擇的答案屆時對你的主角不可用。");
			AddNotRecorded(_tw, "已經歷，但未記錄選擇\n（當時未安裝模組）", "此處之前沒有模組存檔，無法前往。");
			AddUpcomingWord(_tw, "接下來");
			// path ID refusal reasons (PathId.TryApply), inside PathIdBad
			_en[K.PidEmpty] = "it is empty or too long";
			_en[K.PidShape] = "it is not a path ID (BSL1:<length>:<data>:<check>)";
			_en[K.PidLength] = "its length does not match (cut off when copied?)";
			_en[K.PidCheck] = "its check digits do not match (changed or cut off?)";
			_en[K.PidVersion] = "it was made with another version of the game";
			_en[K.PidDamaged] = "it is damaged or incomplete";
			_ru[K.PidEmpty] = "он пуст или слишком длинный";
			_ru[K.PidShape] = "это не ID пути (BSL1:<длина>:<данные>:<проверка>)";
			_ru[K.PidLength] = "длина не совпадает (обрезан при копировании?)";
			_ru[K.PidCheck] = "контрольные цифры не совпадают (изменён или обрезан?)";
			_ru[K.PidVersion] = "он создан в другой версии игры";
			_ru[K.PidDamaged] = "он повреждён или неполон";
			_de[K.PidEmpty] = "sie ist leer oder zu lang";
			_de[K.PidShape] = "das ist keine Pfad-ID (BSL1:<Länge>:<Daten>:<Prüfung>)";
			_de[K.PidLength] = "die Länge stimmt nicht (beim Kopieren abgeschnitten?)";
			_de[K.PidCheck] = "die Prüfziffern stimmen nicht (verändert oder abgeschnitten?)";
			_de[K.PidVersion] = "sie stammt aus einer anderen Version des Spiels";
			_de[K.PidDamaged] = "sie ist beschädigt oder unvollständig";
			_fr[K.PidEmpty] = "il est vide ou trop long";
			_fr[K.PidShape] = "ce n’est pas un ID de parcours (BSL1:<longueur>:<données>:<contrôle>)";
			_fr[K.PidLength] = "la longueur ne correspond pas (coupé à la copie ?)";
			_fr[K.PidCheck] = "les chiffres de contrôle ne correspondent pas (modifié ou coupé ?)";
			_fr[K.PidVersion] = "il a été créé avec une autre version du jeu";
			_fr[K.PidDamaged] = "il est endommagé ou incomplet";
			_es[K.PidEmpty] = "está vacío o es demasiado largo";
			_es[K.PidShape] = "no es un ID de ruta (BSL1:<longitud>:<datos>:<control>)";
			_es[K.PidLength] = "la longitud no coincide (¿se cortó al copiarlo?)";
			_es[K.PidCheck] = "los dígitos de control no coinciden (¿modificado o cortado?)";
			_es[K.PidVersion] = "se creó con otra versión del juego";
			_es[K.PidDamaged] = "está dañado o incompleto";
			_it[K.PidEmpty] = "è vuoto o troppo lungo";
			_it[K.PidShape] = "non è un ID percorso (BSL1:<lunghezza>:<dati>:<controllo>)";
			_it[K.PidLength] = "la lunghezza non corrisponde (tagliato durante la copia?)";
			_it[K.PidCheck] = "le cifre di controllo non corrispondono (modificato o tagliato?)";
			_it[K.PidVersion] = "è stato creato con un’altra versione del gioco";
			_it[K.PidDamaged] = "è danneggiato o incompleto";
			_pl[K.PidEmpty] = "jest pusty lub za długi";
			_pl[K.PidShape] = "to nie jest ID ścieżki (BSL1:<długość>:<dane>:<suma>)";
			_pl[K.PidLength] = "długość się nie zgadza (ucięty przy kopiowaniu?)";
			_pl[K.PidCheck] = "cyfry kontrolne się nie zgadzają (zmieniony lub ucięty?)";
			_pl[K.PidVersion] = "pochodzi z innej wersji gry";
			_pl[K.PidDamaged] = "jest uszkodzony lub niepełny";
			_tr[K.PidEmpty] = "boş ya da çok uzun";
			_tr[K.PidShape] = "bu bir yol kimliği değil (BSL1:<uzunluk>:<veri>:<denetim>)";
			_tr[K.PidLength] = "uzunluk tutmuyor (kopyalanırken kesildi mi?)";
			_tr[K.PidCheck] = "denetim basamakları tutmuyor (değişti ya da kesildi mi?)";
			_tr[K.PidVersion] = "oyunun başka bir sürümüyle oluşturulmuş";
			_tr[K.PidDamaged] = "bozuk ya da eksik";
			_ja[K.PidEmpty] = "空か長すぎます";
			_ja[K.PidShape] = "パスIDではありません（BSL1:<長さ>:<データ>:<チェック>）";
			_ja[K.PidLength] = "長さが一致しません（コピー時に途切れた？）";
			_ja[K.PidCheck] = "チェック値が一致しません（変更または途切れ？）";
			_ja[K.PidVersion] = "別のバージョンのゲームで作られています";
			_ja[K.PidDamaged] = "壊れているか不完全です";
			_ko[K.PidEmpty] = "비어 있거나 너무 깁니다";
			_ko[K.PidShape] = "경로 ID가 아닙니다 (BSL1:<길이>:<데이터>:<검사>)";
			_ko[K.PidLength] = "길이가 맞지 않습니다 (복사할 때 잘렸나요?)";
			_ko[K.PidCheck] = "검사 숫자가 맞지 않습니다 (바뀌었거나 잘렸나요?)";
			_ko[K.PidVersion] = "다른 버전의 게임에서 만들어졌습니다";
			_ko[K.PidDamaged] = "손상되었거나 불완전합니다";
			_zh[K.PidEmpty] = "为空或过长";
			_zh[K.PidShape] = "不是路径 ID（BSL1:<长度>:<数据>:<校验>）";
			_zh[K.PidLength] = "长度不符（复制时被截断？）";
			_zh[K.PidCheck] = "校验码不符（被修改或截断？）";
			_zh[K.PidVersion] = "来自另一个版本的游戏";
			_zh[K.PidDamaged] = "已损坏或不完整";
			_pt[K.PidEmpty] = "está vazio ou longo demais";
			_pt[K.PidShape] = "não é um ID de caminho (BSL1:<tamanho>:<dados>:<verificação>)";
			_pt[K.PidLength] = "o tamanho não confere (cortado ao copiar?)";
			_pt[K.PidCheck] = "os dígitos de verificação não conferem (alterado ou cortado?)";
			_pt[K.PidVersion] = "foi criado com outra versão do jogo";
			_pt[K.PidDamaged] = "está danificado ou incompleto";
			_tw[K.PidEmpty] = "為空或過長";
			_tw[K.PidShape] = "不是路徑 ID（BSL1:<長度>:<資料>:<校驗>）";
			_tw[K.PidLength] = "長度不符（複製時被截斷？）";
			_tw[K.PidCheck] = "校驗碼不符（被修改或截斷？）";
			_tw[K.PidVersion] = "來自另一個版本的遊戲";
			_tw[K.PidDamaged] = "已損壞或不完整";
		}

		private static void AddLoadSceneWord(Dictionary<K, string> d, string loadScene)
		{
			d[K.LoadScene] = loadScene;
		}

		private static void AddSaveKinds(Dictionary<K, string> d, string all, string autosave,
			string checkpoint, string original, string showMore)
		{
			d[K.KindAll] = all;
			d[K.KindAutosave] = autosave;
			d[K.KindCheckpoint] = checkpoint;
			d[K.KindOriginal] = original;
			d[K.ShowMore] = showMore;
		}

		private static void AddPathChecks(Dictionary<K, string> d, string skipped, string badStart)
		{
			d[K.ReplaySkipped] = skipped;
			d[K.StartNotOnPath] = badStart;
		}

		private static void AddKindHelp(Dictionary<K, string> d, string autosave, string checkpoint,
			string manual, string original)
		{
			d[K.HelpAutosave] = autosave;
			d[K.HelpCheckpoint] = checkpoint;
			d[K.HelpManual] = manual;
			d[K.HelpOriginal] = original;
		}

		private static void AddRequirementWords(Dictionary<K, string> d, string requires, string oneOf, string not)
		{
			d[K.Requires] = requires;
			d[K.OneOf] = oneOf;
			d[K.NotWord] = not;
		}

		private static void AddNotRecorded(Dictionary<K, string> d, string text, string noSave)
		{
			d[K.NotRecorded] = text;
			d[K.NoSaveBefore] = noSave;
		}

		private static void AddPickBlocked(Dictionary<K, string> d, string text)
		{
			d[K.PickBlocked] = text;
		}

		private static void AddPossibleOn(Dictionary<K, string> d, string text, string stats,
			string relations, string fates, string events, string and)
		{
			d[K.PossibleOn] = text;
			d[K.OnStats] = stats;
			d[K.OnRelations] = relations;
			d[K.OnFates] = fates;
			d[K.OnEvents] = events;
			d[K.AndWord] = and;
		}

		private static void AddNoStatText(Dictionary<K, string> d, string text)
		{
			d[K.NoStatText] = text;
		}

		private static void AddMissingPick(Dictionary<K, string> d, string text)
		{
			d[K.MissingPick] = text;
		}

		private static void AddReplayStops(Dictionary<K, string> d, string offPath, string death, string stopped)
		{
			d[K.ReplayOffPath] = offPath;
			d[K.ReplayDeath] = death;
			d[K.ReplayStopped] = stopped;
		}

		private static void AddConflict(Dictionary<K, string> d, string conflict, string nothing)
		{
			d[K.PickConflict] = conflict;
			d[K.NothingToLoad] = nothing;
		}

		private static void AddReplayTime(Dictionary<K, string> d, string left, string stop, string sec, string min)
		{
			d[K.ReplayLeft] = left;
			d[K.ReplayStop] = stop;
			d[K.UnitSec] = sec;
			d[K.UnitMin] = min;
		}

		private static void AddReplayTexts(Dictionary<K, string> d, string loading, string progress)
		{
			d[K.ReplayLoading] = loading;
			d[K.ReplayProgress] = progress;
		}

		private static void AddOnThisScene(Dictionary<K, string> d, string text)
		{
			d[K.OnThisScene] = text;
		}

		private static void AddPickNext(Dictionary<K, string> d, string text)
		{
			d[K.PickNext] = text;
		}

		private static void AddResetPath(Dictionary<K, string> d, string text)
		{
			d[K.ResetPath] = text;
		}

		private static void AddUpcomingWord(Dictionary<K, string> d, string upcoming)
		{
			d[K.Upcoming] = upcoming;
		}

		private static Dictionary<K, string> _english;
		private static Dictionary<K, string> _current;
		private static string _currentFor;

		// The translation for the game's current language, English if there
		// is none.
		// The game's own translation of one of its keys, or `fallback`.
		// The game's English for `key` (whatever language is set), or
		// `fallback`: the same for every version of a scene.
		public static string GameEnglish(string key, string fallback)
		{
			try
			{
				string t = LocalizationManager.GetTranslation(key, true, 0, true, false, null, "English");
				return string.IsNullOrEmpty(t) ? fallback : t;
			}
			catch
			{
				return fallback;
			}
		}

		public static string Game(string key, string fallback)
		{
			try
			{
				string t = LocalizationManager.GetTranslation(key);
				return string.IsNullOrEmpty(t) ? fallback : t;
			}
			catch
			{
				return fallback;
			}
		}

		// A scene's title in the game's language, "" when it has none.
		public static string SceneTitle(string scene)
		{
			if (string.IsNullOrEmpty(scene))
			{
				return "";
			}
			// the key the scene's controller shows, where it is not
			// <scene>_Title (054.030.010_Call_Titile, a K row)
			string key = SceneEffects.TitleKey(scene) ?? scene + "_Title";
			string t = Game(key, "");
			return t == key ? "" : t;
		}

		public static string T(K key)
		{
			string lang = Language();
			if (_current == null || lang != _currentFor)
			{
				_current = Table(lang);
				_currentFor = lang;
			}
			string text;
			if (_current != null && _current.TryGetValue(key, out text))
			{
				return text;
			}
			if (_english == null)
			{
				_english = Table("English");
			}
			return _english[key];
		}

		// "English", "Russian", ... (I2's language name), "" if not up yet.
		// `text` negated: "not X", or "X ではない" / "X 아님" / "X değil" in
		// Japanese, Korean and Turkish, which put the negation after.
		public static string Not(string text)
		{
			Dictionary<K, string> t = Table(Language());
			string not = T(K.NotWord);
			return t == _ja || t == _ko || t == _tr ? text + " " + not : not + " " + text;
		}

		public static string Language()
		{
			try
			{
				return LocalizationManager.CurrentLanguage ?? "";
			}
			catch
			{
				return "";
			}
		}

		// Matches on the language code first, then on the name, so both
		// "Chinese (Simplified)" and "zh-CN" resolve.
		private static Dictionary<K, string> Table(string language)
		{
			string code = "en";
			string name = (language ?? "").ToLowerInvariant();
			try
			{
				string c = LocalizationManager.CurrentLanguageCode;
				if (!string.IsNullOrEmpty(c))
				{
					code = c.ToLowerInvariant();
				}
			}
			catch
			{
				// keep the name-based guess
			}
			if (code.StartsWith("ru") || name.Contains("russian")) { return _ru; }
			if (code.StartsWith("de") || name.Contains("german")) { return _de; }
			if (code.StartsWith("fr") || name.Contains("french")) { return _fr; }
			if (code.StartsWith("es") || name.Contains("spanish")) { return _es; }
			if (code.StartsWith("it") || name.Contains("italian")) { return _it; }
			if (code.StartsWith("pl") || name.Contains("polish")) { return _pl; }
			if (code.StartsWith("tr") || name.Contains("turkish")) { return _tr; }
			if (code.StartsWith("ja") || name.Contains("japanese")) { return _ja; }
			if (code.StartsWith("ko") || name.Contains("korean")) { return _ko; }
			if (code.StartsWith("pt") || name.Contains("portuguese")) { return _pt; }
			// Traditional before Simplified: "Chinese (Traditional)", zh-TW/HK/MO, zh-Hant
			if (name.Contains("traditional") || code == "zh-tw" || code == "zh-hk" || code == "zh-mo"
				|| code.StartsWith("zh-hant")) { return _tw; }
			if (code.StartsWith("zh") || name.Contains("chinese")) { return _zh; }
			return _en;
		}

		private static Dictionary<K, string> Make(
			string title, string saveNow, string history, string playthrough,
			string load, string delete, string save, string cancel,
			string confirmLoad, string confirmDelete, string confirmSave, string emptySaves,
			string colType, string colSaved, string colChapter, string colAge, string colYear,
			string colSeason, string colTurn, string colScene, string colChoice,
			string typeAuto, string typeManual, string current, string nothingYet)
		{
			return new Dictionary<K, string>
			{
				{ K.Title, title },
				{ K.SaveNow, saveNow },
				{ K.History, history },
				{ K.Playthrough, playthrough },
				{ K.Load, load },
				{ K.Delete, delete },
				{ K.Save, save },
				{ K.Cancel, cancel },
				{ K.ConfirmLoad, confirmLoad },
				{ K.ConfirmDelete, confirmDelete },
				{ K.ConfirmSave, confirmSave },
				{ K.EmptySaves, emptySaves },
				{ K.ColType, colType },
				{ K.ColSaved, colSaved },
				{ K.ColChapter, colChapter },
				{ K.ColAge, colAge },
				{ K.ColYear, colYear },
				{ K.ColSeason, colSeason },
				{ K.ColTurn, colTurn },
				{ K.ColScene, colScene },
				{ K.ColChoice, colChoice },
				{ K.TypeAuto, typeAuto },
				{ K.TypeManual, typeManual },
				{ K.Current, current },
				{ K.NothingYet, nothingYet }
			};
		}

		private static readonly Dictionary<K, string> _en = Make(
			"Save / Load", "Save now", "Tree view", "Playthrough",
			"Load", "Delete", "Save", "Cancel",
			"Load this save?\n\n{0}",
			"Remove this save from the list?\n\n{0}\n\nThe file is deleted. Your game saves are not touched.",
			"Save the current game now?\n\nIt is added to the list as a new row. Your game saves are not touched.",
			"No saves yet. Choice points are recorded automatically; use \"Save now\" below.",
			"Save type", "Saved", "Chapter", "Age", "Year", "Season", "Turn", "Scene", "Choice",
			"Auto", "Manual", "current", "Nothing recorded yet.");

		private static readonly Dictionary<K, string> _ru = Make(
			"Сохранить / Загрузить", "Сохранить", "Дерево выборов", "Прохождение",
			"Загрузить", "Удалить", "Сохранить", "Отмена",
			"Загрузить это сохранение?\n\n{0}",
			"Убрать это сохранение из списка?\n\n{0}\n\nФайл будет удалён. Сохранения игры не затрагиваются.",
			"Сохранить игру сейчас?\n\nОно добавится в список новой строкой. Сохранения игры не затрагиваются.",
			"Сохранений пока нет. Точки выбора записываются автоматически; используйте «Сохранить» ниже.",
			"Тип", "Сохранено", "Глава", "Возраст", "Год", "Сезон", "Ход", "Сцена", "Выбор",
			"Авто", "Вручную", "текущая", "Пока ничего не записано.");

		private static readonly Dictionary<K, string> _de = Make(
			"Speichern / Laden", "Jetzt speichern", "Baumansicht", "Durchlauf",
			"Laden", "Löschen", "Speichern", "Abbrechen",
			"Diesen Spielstand laden?\n\n{0}",
			"Diesen Spielstand aus der Liste entfernen?\n\n{0}\n\nDie Datei wird gelöscht. Deine Spielstände des Spiels bleiben unberührt.",
			"Das aktuelle Spiel jetzt speichern?\n\nEs wird als neue Zeile hinzugefügt. Deine Spielstände des Spiels bleiben unberührt.",
			"Noch keine Spielstände. Entscheidungspunkte werden automatisch aufgezeichnet; nutze \"Jetzt speichern\" unten.",
			"Typ", "Gespeichert", "Kapitel", "Alter", "Jahr", "Jahreszeit", "Zug", "Szene", "Wahl",
			"Auto", "Manuell", "aktuell", "Noch nichts aufgezeichnet.");

		private static readonly Dictionary<K, string> _fr = Make(
			"Enregistrer / Charger", "Enregistrer", "Arborescence", "Partie",
			"Charger", "Supprimer", "Enregistrer", "Annuler",
			"Charger cette sauvegarde ?\n\n{0}",
			"Retirer cette sauvegarde de la liste ?\n\n{0}\n\nLe fichier est supprimé. Vos sauvegardes du jeu ne sont pas touchées.",
			"Enregistrer la partie en cours ?\n\nElle sera ajoutée à la liste. Vos sauvegardes du jeu ne sont pas touchées.",
			"Aucune sauvegarde. Les points de choix sont enregistrés automatiquement ; utilisez « Enregistrer » ci-dessous.",
			"Type", "Enregistré", "Chapitre", "Âge", "Année", "Saison", "Tour", "Scène", "Choix",
			"Auto", "Manuel", "actuelle", "Rien d'enregistré pour l'instant.");

		private static readonly Dictionary<K, string> _es = Make(
			"Guardar / Cargar", "Guardar ahora", "Vista de árbol", "Partida",
			"Cargar", "Eliminar", "Guardar", "Cancelar",
			"¿Cargar esta partida guardada?\n\n{0}",
			"¿Quitar esta partida de la lista?\n\n{0}\n\nEl archivo se elimina. Tus partidas del juego no se tocan.",
			"¿Guardar la partida actual?\n\nSe añadirá como una fila nueva. Tus partidas del juego no se tocan.",
			"Aún no hay partidas guardadas. Los puntos de decisión se registran automáticamente; usa «Guardar ahora» abajo.",
			"Tipo", "Guardado", "Capítulo", "Edad", "Año", "Estación", "Turno", "Escena", "Elección",
			"Auto", "Manual", "actual", "Nada registrado todavía.");

		private static readonly Dictionary<K, string> _it = Make(
			"Salva / Carica", "Salva ora", "Vista ad albero", "Partita",
			"Carica", "Elimina", "Salva", "Annulla",
			"Caricare questo salvataggio?\n\n{0}",
			"Rimuovere questo salvataggio dall'elenco?\n\n{0}\n\nIl file viene eliminato. I salvataggi del gioco non vengono toccati.",
			"Salvare la partita in corso?\n\nVerrà aggiunta come nuova riga. I salvataggi del gioco non vengono toccati.",
			"Nessun salvataggio. I punti di scelta vengono registrati automaticamente; usa «Salva ora» sotto.",
			"Tipo", "Salvato", "Capitolo", "Età", "Anno", "Stagione", "Turno", "Scena", "Scelta",
			"Auto", "Manuale", "attuale", "Niente di registrato.");

		private static readonly Dictionary<K, string> _pl = Make(
			"Zapisz / Wczytaj", "Zapisz teraz", "Widok drzewa", "Rozgrywka",
			"Wczytaj", "Usuń", "Zapisz", "Anuluj",
			"Wczytać to zapisanie?\n\n{0}",
			"Usunąć to zapisanie z listy?\n\n{0}\n\nPlik zostanie usunięty. Zapisania gry pozostaną nietknięte.",
			"Zapisać bieżącą grę?\n\nZostanie dodane jako nowy wiersz. Zapisania gry pozostaną nietknięte.",
			"Brak zapisań. Punkty wyboru są zapisywane automatycznie; użyj „Zapisz teraz” poniżej.",
			"Typ", "Zapisano", "Rozdział", "Wiek", "Rok", "Pora roku", "Tura", "Scena", "Wybór",
			"Auto", "Ręcznie", "bieżąca", "Nic jeszcze nie zapisano.");

		private static readonly Dictionary<K, string> _tr = Make(
			"Kaydet / Yükle", "Şimdi kaydet", "Ağaç görünümü", "Oyun",
			"Yükle", "Sil", "Kaydet", "İptal",
			"Bu kayıt yüklensin mi?\n\n{0}",
			"Bu kayıt listeden kaldırılsın mı?\n\n{0}\n\nDosya silinir. Oyunun kayıtlarına dokunulmaz.",
			"Oyun şimdi kaydedilsin mi?\n\nListeye yeni bir satır olarak eklenir. Oyunun kayıtlarına dokunulmaz.",
			"Henüz kayıt yok. Seçim noktaları otomatik kaydedilir; aşağıdaki «Şimdi kaydet»i kullanın.",
			"Tür", "Kayıt", "Bölüm", "Yaş", "Yıl", "Mevsim", "Tur", "Sahne", "Seçim",
			"Otomatik", "Elle", "geçerli", "Henüz bir şey kaydedilmedi.");

		private static readonly Dictionary<K, string> _ja = Make(
			"保存 / 読み込み", "今すぐ保存", "ツリー表示", "プレイ記録",
			"読み込む", "削除", "保存", "キャンセル",
			"このセーブを読み込みますか？\n\n{0}",
			"このセーブを一覧から削除しますか？\n\n{0}\n\nファイルが削除されます。ゲームのセーブは変更されません。",
			"現在のゲームを保存しますか？\n\n一覧に新しい行として追加されます。ゲームのセーブは変更されません。",
			"まだセーブがありません。選択地点は自動で記録されます。下の「今すぐ保存」を使ってください。",
			"種類", "保存日時", "章", "年齢", "年", "季節", "ターン", "シーン", "選択",
			"自動", "手動", "現在", "まだ記録がありません。");

		private static readonly Dictionary<K, string> _ko = Make(
			"저장 / 불러오기", "지금 저장", "트리 보기", "플레이 기록",
			"불러오기", "삭제", "저장", "취소",
			"이 저장을 불러올까요?\n\n{0}",
			"이 저장을 목록에서 삭제할까요?\n\n{0}\n\n파일이 삭제됩니다. 게임 저장은 건드리지 않습니다.",
			"현재 게임을 저장할까요?\n\n목록에 새 줄로 추가됩니다. 게임 저장은 건드리지 않습니다.",
			"아직 저장이 없습니다. 선택 지점은 자동으로 기록됩니다. 아래의 «지금 저장»을 사용하세요.",
			"종류", "저장 시각", "장", "나이", "연도", "계절", "턴", "장면", "선택",
			"자동", "수동", "현재", "아직 기록이 없습니다.");

		private static readonly Dictionary<K, string> _zh = Make(
			"保存 / 读取", "立即保存", "树状视图", "游戏历程",
			"读取", "删除", "保存", "取消",
			"读取此存档？\n\n{0}",
			"从列表中移除此存档？\n\n{0}\n\n文件将被删除。游戏存档不受影响。",
			"现在保存游戏？\n\n将作为新的一行加入列表。游戏存档不受影响。",
			"还没有存档。选择点会自动记录；请使用下方的“立即保存”。",
			"类型", "保存时间", "章节", "年龄", "年份", "季节", "回合", "场景", "选择",
			"自动", "手动", "当前", "还没有记录。");

		private static readonly Dictionary<K, string> _pt = Make(
			"Salvar / Carregar", "Salvar agora", "Visualização em árvore", "Partida",
			"Carregar", "Excluir", "Salvar", "Cancelar",
			"Carregar este save?\n\n{0}",
			"Remover este save da lista?\n\n{0}\n\nO arquivo é excluído. Os saves do jogo não são alterados.",
			"Salvar o jogo atual agora?\n\nEle é adicionado à lista como uma nova linha. Os saves do jogo não são alterados.",
			"Ainda não há saves. Os pontos de escolha são gravados automaticamente; use \"Salvar agora\" abaixo.",
			"Tipo de save", "Salvo em", "Capítulo", "Idade", "Ano", "Estação", "Turno", "Cena", "Escolha",
			"Auto", "Manual", "atual", "Nada gravado ainda.");

		private static readonly Dictionary<K, string> _tw = Make(
			"存檔 / 載入", "立即存檔", "樹狀檢視", "遊戲歷程",
			"載入", "刪除", "存檔", "取消",
			"載入此存檔？\n\n{0}",
			"從清單中移除此存檔？\n\n{0}\n\n檔案將被刪除。遊戲存檔不受影響。",
			"現在儲存遊戲？\n\n將作為新的一列加入清單。遊戲存檔不受影響。",
			"還沒有存檔。選擇點會自動記錄；請使用下方的「立即存檔」。",
			"類型", "存檔時間", "章節", "年齡", "年份", "季節", "回合", "場景", "選擇",
			"自動", "手動", "目前", "還沒有記錄。");
	}
}
