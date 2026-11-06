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
			LoadScene, Upcoming, NotRecorded, Requires, OneOf, NotWord, NoSaveBefore, PickBlocked, ResetPath, PickNext, OnThisScene,
			PickConflict, NothingToLoad,
			PossibleOn, OnStats, OnRelations, OnFates, OnEvents, AndWord, NoStatText, MissingPick, ReplayOffPath, ReplayDeath, ReplayStopped,
			ReplaySkipped, StartNotOnPath,
			HelpAutosave, HelpCheckpoint, HelpManual, HelpOriginal,
			
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
			string t = Game(scene + "_Title", "");
			return t == scene + "_Title" ? "" : t;
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
		private static string Language()
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
	}
}
