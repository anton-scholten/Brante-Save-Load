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
			Title, SaveNow, History, Playthrough, PlaythroughSub, Close,
			Load, Delete, Save, Cancel,
			ConfirmLoad, ConfirmDelete, ConfirmSave, EmptySaves,
			ColType, ColSaved, ColChapter, ColAge, ColYear, ColSeason, ColTurn, ColScene, ColChoice,
			TypeAuto, TypeManual, Current, NothingYet,
			LoadScene, Upcoming, Possible, NotRecorded, Requires, OneOf, NotWord, NoSaveBefore, PickBlocked, ResetPath, BookTab,
			PickConflict, NothingToLoad,
			PossibleOn, OnStats, OnRelations, OnFates, OnEvents, AndWord, NoStatText
		}

		static Loc()
		{
			AddLoadSceneWord(_en, "Load scene");
			AddLoadSceneWord(_ru, "Загрузить сцену");
			AddLoadSceneWord(_de, "Szene laden");
			AddLoadSceneWord(_fr, "Charger la scène");
			AddLoadSceneWord(_es, "Cargar escena");
			AddLoadSceneWord(_it, "Carica scena");
			AddLoadSceneWord(_pl, "Wczytaj scenę");
			AddLoadSceneWord(_tr, "Sahneyi yükle");
			AddLoadSceneWord(_ja, "シーンを読み込む");
			AddLoadSceneWord(_ko, "장면 불러오기");
			AddLoadSceneWord(_zh, "读取场景");
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
			AddResetPath(_en, "Reset path", "save\nload\nmod");
			AddResetPath(_ru, "Сбросить путь", "сохр.\nзагр.\nмод");
			AddResetPath(_de, "Pfad zurücksetzen", "speich.\nladen\nmod");
			AddResetPath(_fr, "Réinitialiser le chemin", "sauv.\ncharger\nmod");
			AddResetPath(_es, "Restablecer el camino", "guardar\ncargar\nmod");
			AddResetPath(_it, "Ripristina il percorso", "salva\ncarica\nmod");
			AddResetPath(_pl, "Resetuj ścieżkę", "zapis\nwczyt.\nmod");
			AddResetPath(_tr, "Yolu sıfırla", "kaydet\nyükle\nmod");
			AddResetPath(_ja, "道筋をリセット", "セーブ\nロード\nMOD");
			AddResetPath(_ko, "경로 초기화", "저장\n불러오기\n모드");
			AddResetPath(_zh, "重置路径", "存档\n读档\n模组");
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
			AddUpcomingWord(_en, "upcoming", "possible");
			AddUpcomingWord(_ru, "впереди", "возможно");
			AddUpcomingWord(_de, "kommt noch", "möglich");
			AddUpcomingWord(_fr, "à venir", "possible");
			AddUpcomingWord(_es, "por venir", "posible");
			AddUpcomingWord(_it, "in arrivo", "possibile");
			AddUpcomingWord(_pl, "przed tobą", "możliwe");
			AddUpcomingWord(_tr, "sırada", "olası");
			AddUpcomingWord(_ja, "この先", "条件次第");
			AddUpcomingWord(_ko, "앞으로", "조건부");
			AddUpcomingWord(_zh, "接下来", "视条件而定");
		}

		private static void AddLoadSceneWord(Dictionary<K, string> d, string loadScene)
		{
			d[K.LoadScene] = loadScene;
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

		private static void AddConflict(Dictionary<K, string> d, string conflict, string nothing)
		{
			d[K.PickConflict] = conflict;
			d[K.NothingToLoad] = nothing;
		}

		private static void AddResetPath(Dictionary<K, string> d, string text, string bookTab)
		{
			d[K.ResetPath] = text;
			d[K.BookTab] = bookTab;
		}

		private static void AddUpcomingWord(Dictionary<K, string> d, string upcoming, string possible)
		{
			d[K.Upcoming] = upcoming;
			d[K.Possible] = possible;
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
			string title, string saveNow, string history, string playthrough, string playthroughSub,
			string close, string load, string delete, string save, string cancel,
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
				{ K.PlaythroughSub, playthroughSub },
				{ K.Close, close },
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
			"The scenes this save has been through, your answers highlighted, then what lies ahead.",
			"Close", "Load", "Delete", "Save", "Cancel",
			"Load this save?\n\n{0}\n\nYour original save will not be modified.",
			"Remove this save from the list?\n\n{0}\n\nThe file is deleted. Your game saves are not touched.",
			"Save the current game now?\n\nIt is added to the list as a new row. Your game saves are not touched.",
			"No saves yet. Choice points are recorded automatically; use \"Save now\" below.",
			"Save type", "Saved", "Chapter", "Age", "Year", "Season", "Turn", "Scene", "Choice",
			"Auto", "Manual", "current", "Nothing recorded yet.");

		private static readonly Dictionary<K, string> _ru = Make(
			"Сохранить / Загрузить", "Сохранить", "Дерево выборов", "Прохождение",
			"Сцены, которые прошло это сохранение, с вашими ответами, и то, что ждёт впереди.",
			"Закрыть", "Загрузить", "Удалить", "Сохранить", "Отмена",
			"Загрузить это сохранение?\n\n{0}\n\nВаше исходное сохранение не будет изменено.",
			"Убрать это сохранение из списка?\n\n{0}\n\nФайл будет удалён. Сохранения игры не затрагиваются.",
			"Сохранить игру сейчас?\n\nОно добавится в список новой строкой. Сохранения игры не затрагиваются.",
			"Сохранений пока нет. Точки выбора записываются автоматически; используйте «Сохранить» ниже.",
			"Тип", "Сохранено", "Глава", "Возраст", "Год", "Сезон", "Ход", "Сцена", "Выбор",
			"Авто", "Вручную", "текущая", "Пока ничего не записано.");

		private static readonly Dictionary<K, string> _de = Make(
			"Speichern / Laden", "Jetzt speichern", "Baumansicht", "Durchlauf",
			"Die Szenen dieses Spielstands mit deinen Antworten, danach, was noch kommt.",
			"Schließen", "Laden", "Löschen", "Speichern", "Abbrechen",
			"Diesen Spielstand laden?\n\n{0}\n\nDein ursprünglicher Spielstand wird nicht verändert.",
			"Diesen Spielstand aus der Liste entfernen?\n\n{0}\n\nDie Datei wird gelöscht. Deine Spielstände des Spiels bleiben unberührt.",
			"Das aktuelle Spiel jetzt speichern?\n\nEs wird als neue Zeile hinzugefügt. Deine Spielstände des Spiels bleiben unberührt.",
			"Noch keine Spielstände. Entscheidungspunkte werden automatisch aufgezeichnet; nutze \"Jetzt speichern\" unten.",
			"Typ", "Gespeichert", "Kapitel", "Alter", "Jahr", "Jahreszeit", "Zug", "Szene", "Wahl",
			"Auto", "Manuell", "aktuell", "Noch nichts aufgezeichnet.");

		private static readonly Dictionary<K, string> _fr = Make(
			"Enregistrer / Charger", "Enregistrer", "Arborescence", "Partie",
			"Les scènes de cette sauvegarde avec vos réponses, puis ce qui vous attend.",
			"Fermer", "Charger", "Supprimer", "Enregistrer", "Annuler",
			"Charger cette sauvegarde ?\n\n{0}\n\nVotre sauvegarde d'origine ne sera pas modifiée.",
			"Retirer cette sauvegarde de la liste ?\n\n{0}\n\nLe fichier est supprimé. Vos sauvegardes du jeu ne sont pas touchées.",
			"Enregistrer la partie en cours ?\n\nElle sera ajoutée à la liste. Vos sauvegardes du jeu ne sont pas touchées.",
			"Aucune sauvegarde. Les points de choix sont enregistrés automatiquement ; utilisez « Enregistrer » ci-dessous.",
			"Type", "Enregistré", "Chapitre", "Âge", "Année", "Saison", "Tour", "Scène", "Choix",
			"Auto", "Manuel", "actuelle", "Rien d'enregistré pour l'instant.");

		private static readonly Dictionary<K, string> _es = Make(
			"Guardar / Cargar", "Guardar ahora", "Vista de árbol", "Partida",
			"Las escenas de esta partida con tus respuestas, y lo que queda por delante.",
			"Cerrar", "Cargar", "Eliminar", "Guardar", "Cancelar",
			"¿Cargar esta partida guardada?\n\n{0}\n\nTu partida guardada original no se modificará.",
			"¿Quitar esta partida de la lista?\n\n{0}\n\nEl archivo se elimina. Tus partidas del juego no se tocan.",
			"¿Guardar la partida actual?\n\nSe añadirá como una fila nueva. Tus partidas del juego no se tocan.",
			"Aún no hay partidas guardadas. Los puntos de decisión se registran automáticamente; usa «Guardar ahora» abajo.",
			"Tipo", "Guardado", "Capítulo", "Edad", "Año", "Estación", "Turno", "Escena", "Elección",
			"Auto", "Manual", "actual", "Nada registrado todavía.");

		private static readonly Dictionary<K, string> _it = Make(
			"Salva / Carica", "Salva ora", "Vista ad albero", "Partita",
			"Le scene di questo salvataggio con le tue risposte, poi ciò che ti attende.",
			"Chiudi", "Carica", "Elimina", "Salva", "Annulla",
			"Caricare questo salvataggio?\n\n{0}\n\nIl salvataggio originale non verrà modificato.",
			"Rimuovere questo salvataggio dall'elenco?\n\n{0}\n\nIl file viene eliminato. I salvataggi del gioco non vengono toccati.",
			"Salvare la partita in corso?\n\nVerrà aggiunta come nuova riga. I salvataggi del gioco non vengono toccati.",
			"Nessun salvataggio. I punti di scelta vengono registrati automaticamente; usa «Salva ora» sotto.",
			"Tipo", "Salvato", "Capitolo", "Età", "Anno", "Stagione", "Turno", "Scena", "Scelta",
			"Auto", "Manuale", "attuale", "Niente di registrato.");

		private static readonly Dictionary<K, string> _pl = Make(
			"Zapisz / Wczytaj", "Zapisz teraz", "Widok drzewa", "Rozgrywka",
			"Sceny tego zapisu z twoimi odpowiedziami, a potem to, co przed tobą.",
			"Zamknij", "Wczytaj", "Usuń", "Zapisz", "Anuluj",
			"Wczytać to zapisanie?\n\n{0}\n\nTwoje pierwotne zapisanie nie zostanie zmienione.",
			"Usunąć to zapisanie z listy?\n\n{0}\n\nPlik zostanie usunięty. Zapisania gry pozostaną nietknięte.",
			"Zapisać bieżącą grę?\n\nZostanie dodane jako nowy wiersz. Zapisania gry pozostaną nietknięte.",
			"Brak zapisań. Punkty wyboru są zapisywane automatycznie; użyj „Zapisz teraz” poniżej.",
			"Typ", "Zapisano", "Rozdział", "Wiek", "Rok", "Pora roku", "Tura", "Scena", "Wybór",
			"Auto", "Ręcznie", "bieżąca", "Nic jeszcze nie zapisano.");

		private static readonly Dictionary<K, string> _tr = Make(
			"Kaydet / Yükle", "Şimdi kaydet", "Ağaç görünümü", "Oyun",
			"Bu kaydın geçtiği sahneler ve cevapların, ardından seni bekleyenler.",
			"Kapat", "Yükle", "Sil", "Kaydet", "İptal",
			"Bu kayıt yüklensin mi?\n\n{0}\n\nÖzgün kaydınız değiştirilmeyecek.",
			"Bu kayıt listeden kaldırılsın mı?\n\n{0}\n\nDosya silinir. Oyunun kayıtlarına dokunulmaz.",
			"Oyun şimdi kaydedilsin mi?\n\nListeye yeni bir satır olarak eklenir. Oyunun kayıtlarına dokunulmaz.",
			"Henüz kayıt yok. Seçim noktaları otomatik kaydedilir; aşağıdaki «Şimdi kaydet»i kullanın.",
			"Tür", "Kayıt", "Bölüm", "Yaş", "Yıl", "Mevsim", "Tur", "Sahne", "Seçim",
			"Otomatik", "Elle", "geçerli", "Henüz bir şey kaydedilmedi.");

		private static readonly Dictionary<K, string> _ja = Make(
			"保存 / 読み込み", "今すぐ保存", "ツリー表示", "プレイ記録",
			"このセーブが通ったシーンとあなたの答え、そしてこの先の展開。",
			"閉じる", "読み込む", "削除", "保存", "キャンセル",
			"このセーブを読み込みますか？\n\n{0}\n\n元のセーブは変更されません。",
			"このセーブを一覧から削除しますか？\n\n{0}\n\nファイルが削除されます。ゲームのセーブは変更されません。",
			"現在のゲームを保存しますか？\n\n一覧に新しい行として追加されます。ゲームのセーブは変更されません。",
			"まだセーブがありません。選択地点は自動で記録されます。下の「今すぐ保存」を使ってください。",
			"種類", "保存日時", "章", "年齢", "年", "季節", "ターン", "シーン", "選択",
			"自動", "手動", "現在", "まだ記録がありません。");

		private static readonly Dictionary<K, string> _ko = Make(
			"저장 / 불러오기", "지금 저장", "트리 보기", "플레이 기록",
			"이 저장이 지나온 장면과 당신의 답, 그리고 앞으로의 장면.",
			"닫기", "불러오기", "삭제", "저장", "취소",
			"이 저장을 불러올까요?\n\n{0}\n\n원래 저장은 변경되지 않습니다.",
			"이 저장을 목록에서 삭제할까요?\n\n{0}\n\n파일이 삭제됩니다. 게임 저장은 건드리지 않습니다.",
			"현재 게임을 저장할까요?\n\n목록에 새 줄로 추가됩니다. 게임 저장은 건드리지 않습니다.",
			"아직 저장이 없습니다. 선택 지점은 자동으로 기록됩니다. 아래의 «지금 저장»을 사용하세요.",
			"종류", "저장 시각", "장", "나이", "연도", "계절", "턴", "장면", "선택",
			"자동", "수동", "현재", "아직 기록이 없습니다.");

		private static readonly Dictionary<K, string> _zh = Make(
			"保存 / 读取", "立即保存", "树状视图", "游戏历程",
			"此存档经历过的场景及你的选择，以及接下来的内容。",
			"关闭", "读取", "删除", "保存", "取消",
			"读取此存档？\n\n{0}\n\n原始存档不会被修改。",
			"从列表中移除此存档？\n\n{0}\n\n文件将被删除。游戏存档不受影响。",
			"现在保存游戏？\n\n将作为新的一行加入列表。游戏存档不受影响。",
			"还没有存档。选择点会自动记录；请使用下方的“立即保存”。",
			"类型", "保存时间", "章节", "年龄", "年份", "季节", "回合", "场景", "选择",
			"自动", "手动", "当前", "还没有记录。");
	}
}
