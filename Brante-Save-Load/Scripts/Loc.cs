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
			TypeAuto, TypeManual, SaveAvailable, Current, NothingYet,
			TreeChosen, TreeOffered, Explore, LoadScene, PickAnswer, Upcoming, Possible
		}

		static Loc()
		{
			AddTreeWords(_en, "chosen", "offered");
			AddTreeWords(_ru, "выбрано", "предложено");
			AddTreeWords(_de, "gewählt", "angeboten");
			AddTreeWords(_fr, "choisi", "proposé");
			AddTreeWords(_es, "elegido", "ofrecido");
			AddTreeWords(_it, "scelto", "offerto");
			AddTreeWords(_pl, "wybrano", "zaproponowano");
			AddTreeWords(_tr, "seçildi", "sunuldu");
			AddTreeWords(_ja, "選択済み", "候補");
			AddTreeWords(_ko, "선택됨", "제시됨");
			AddTreeWords(_zh, "已选择", "可选");
			AddExploreWords(_en, "Explore", "Load this scene");
			AddExploreWords(_ru, "Обзор", "Загрузить эту сцену");
			AddExploreWords(_de, "Erkunden", "Diese Szene laden");
			AddExploreWords(_fr, "Explorer", "Charger cette scène");
			AddExploreWords(_es, "Explorar", "Cargar esta escena");
			AddExploreWords(_it, "Esplora", "Carica questa scena");
			AddExploreWords(_pl, "Odkrywaj", "Wczytaj tę scenę");
			AddExploreWords(_tr, "Keşfet", "Bu sahneyi yükle");
			AddExploreWords(_ja, "探索", "このシーンを読み込む");
			AddExploreWords(_ko, "탐색", "이 장면 불러오기");
			AddExploreWords(_zh, "探索", "读取此场景");
			AddHintWords(_en, "Pick an answer to continue the path");
			AddHintWords(_ru, "Выберите ответ, чтобы продолжить путь");
			AddHintWords(_de, "Wähle eine Antwort, um den Pfad fortzusetzen");
			AddHintWords(_fr, "Choisissez une réponse pour continuer le chemin");
			AddHintWords(_es, "Elige una respuesta para continuar el camino");
			AddHintWords(_it, "Scegli una risposta per continuare il percorso");
			AddHintWords(_pl, "Wybierz odpowiedź, aby kontynuować ścieżkę");
			AddHintWords(_tr, "Yola devam etmek için bir yanıt seçin");
			AddHintWords(_ja, "答えを選ぶと道が続きます");
			AddHintWords(_ko, "계속하려면 답을 선택하세요");
			AddHintWords(_zh, "选择一个答案以继续路径");
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

		private static void AddTreeWords(Dictionary<K, string> d, string chosen, string offered)
		{
			d[K.TreeChosen] = chosen;
			d[K.TreeOffered] = offered;
		}

		private static void AddExploreWords(Dictionary<K, string> d, string explore, string loadScene)
		{
			d[K.Explore] = explore;
			d[K.LoadScene] = loadScene;
		}

		private static void AddUpcomingWord(Dictionary<K, string> d, string upcoming, string possible)
		{
			d[K.Upcoming] = upcoming;
			d[K.Possible] = possible;
		}

		private static void AddHintWords(Dictionary<K, string> d, string pickAnswer)
		{
			d[K.PickAnswer] = pickAnswer;
		}

		private static Dictionary<K, string> _english;
		private static Dictionary<K, string> _current;
		private static string _currentFor;

		// The translation for the game's current language, English if there
		// is none.
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
			string typeAuto, string typeManual, string saveAvailable, string current, string nothingYet)
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
				{ K.SaveAvailable, saveAvailable },
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
			"Auto", "Manual", "save available", "current", "Nothing recorded yet.");

		private static readonly Dictionary<K, string> _ru = Make(
			"Сохранить / Загрузить", "Сохранить", "Дерево выборов", "Прохождение",
			"Сцены, которые прошло это сохранение, с вашими ответами, и то, что ждёт впереди.",
			"Закрыть", "Загрузить", "Удалить", "Сохранить", "Отмена",
			"Загрузить это сохранение?\n\n{0}\n\nВаше исходное сохранение не будет изменено.",
			"Убрать это сохранение из списка?\n\n{0}\n\nФайл будет удалён. Сохранения игры не затрагиваются.",
			"Сохранить игру сейчас?\n\nОно добавится в список новой строкой. Сохранения игры не затрагиваются.",
			"Сохранений пока нет. Точки выбора записываются автоматически; используйте «Сохранить» ниже.",
			"Тип", "Сохранено", "Глава", "Возраст", "Год", "Сезон", "Ход", "Сцена", "Выбор",
			"Авто", "Вручную", "есть сохранение", "текущая", "Пока ничего не записано.");

		private static readonly Dictionary<K, string> _de = Make(
			"Speichern / Laden", "Jetzt speichern", "Baumansicht", "Durchlauf",
			"Die Szenen dieses Spielstands mit deinen Antworten, danach, was noch kommt.",
			"Schließen", "Laden", "Löschen", "Speichern", "Abbrechen",
			"Diesen Spielstand laden?\n\n{0}\n\nDein ursprünglicher Spielstand wird nicht verändert.",
			"Diesen Spielstand aus der Liste entfernen?\n\n{0}\n\nDie Datei wird gelöscht. Deine Spielstände des Spiels bleiben unberührt.",
			"Das aktuelle Spiel jetzt speichern?\n\nEs wird als neue Zeile hinzugefügt. Deine Spielstände des Spiels bleiben unberührt.",
			"Noch keine Spielstände. Entscheidungspunkte werden automatisch aufgezeichnet; nutze \"Jetzt speichern\" unten.",
			"Typ", "Gespeichert", "Kapitel", "Alter", "Jahr", "Jahreszeit", "Zug", "Szene", "Wahl",
			"Auto", "Manuell", "Spielstand vorhanden", "aktuell", "Noch nichts aufgezeichnet.");

		private static readonly Dictionary<K, string> _fr = Make(
			"Enregistrer / Charger", "Enregistrer", "Arborescence", "Partie",
			"Les scènes de cette sauvegarde avec vos réponses, puis ce qui vous attend.",
			"Fermer", "Charger", "Supprimer", "Enregistrer", "Annuler",
			"Charger cette sauvegarde ?\n\n{0}\n\nVotre sauvegarde d'origine ne sera pas modifiée.",
			"Retirer cette sauvegarde de la liste ?\n\n{0}\n\nLe fichier est supprimé. Vos sauvegardes du jeu ne sont pas touchées.",
			"Enregistrer la partie en cours ?\n\nElle sera ajoutée à la liste. Vos sauvegardes du jeu ne sont pas touchées.",
			"Aucune sauvegarde. Les points de choix sont enregistrés automatiquement ; utilisez « Enregistrer » ci-dessous.",
			"Type", "Enregistré", "Chapitre", "Âge", "Année", "Saison", "Tour", "Scène", "Choix",
			"Auto", "Manuel", "sauvegarde disponible", "actuelle", "Rien d'enregistré pour l'instant.");

		private static readonly Dictionary<K, string> _es = Make(
			"Guardar / Cargar", "Guardar ahora", "Vista de árbol", "Partida",
			"Las escenas de esta partida con tus respuestas, y lo que queda por delante.",
			"Cerrar", "Cargar", "Eliminar", "Guardar", "Cancelar",
			"¿Cargar esta partida guardada?\n\n{0}\n\nTu partida guardada original no se modificará.",
			"¿Quitar esta partida de la lista?\n\n{0}\n\nEl archivo se elimina. Tus partidas del juego no se tocan.",
			"¿Guardar la partida actual?\n\nSe añadirá como una fila nueva. Tus partidas del juego no se tocan.",
			"Aún no hay partidas guardadas. Los puntos de decisión se registran automáticamente; usa «Guardar ahora» abajo.",
			"Tipo", "Guardado", "Capítulo", "Edad", "Año", "Estación", "Turno", "Escena", "Elección",
			"Auto", "Manual", "guardado disponible", "actual", "Nada registrado todavía.");

		private static readonly Dictionary<K, string> _it = Make(
			"Salva / Carica", "Salva ora", "Vista ad albero", "Partita",
			"Le scene di questo salvataggio con le tue risposte, poi ciò che ti attende.",
			"Chiudi", "Carica", "Elimina", "Salva", "Annulla",
			"Caricare questo salvataggio?\n\n{0}\n\nIl salvataggio originale non verrà modificato.",
			"Rimuovere questo salvataggio dall'elenco?\n\n{0}\n\nIl file viene eliminato. I salvataggi del gioco non vengono toccati.",
			"Salvare la partita in corso?\n\nVerrà aggiunta come nuova riga. I salvataggi del gioco non vengono toccati.",
			"Nessun salvataggio. I punti di scelta vengono registrati automaticamente; usa «Salva ora» sotto.",
			"Tipo", "Salvato", "Capitolo", "Età", "Anno", "Stagione", "Turno", "Scena", "Scelta",
			"Auto", "Manuale", "salvataggio disponibile", "attuale", "Niente di registrato.");

		private static readonly Dictionary<K, string> _pl = Make(
			"Zapisz / Wczytaj", "Zapisz teraz", "Widok drzewa", "Rozgrywka",
			"Sceny tego zapisu z twoimi odpowiedziami, a potem to, co przed tobą.",
			"Zamknij", "Wczytaj", "Usuń", "Zapisz", "Anuluj",
			"Wczytać to zapisanie?\n\n{0}\n\nTwoje pierwotne zapisanie nie zostanie zmienione.",
			"Usunąć to zapisanie z listy?\n\n{0}\n\nPlik zostanie usunięty. Zapisania gry pozostaną nietknięte.",
			"Zapisać bieżącą grę?\n\nZostanie dodane jako nowy wiersz. Zapisania gry pozostaną nietknięte.",
			"Brak zapisań. Punkty wyboru są zapisywane automatycznie; użyj „Zapisz teraz” poniżej.",
			"Typ", "Zapisano", "Rozdział", "Wiek", "Rok", "Pora roku", "Tura", "Scena", "Wybór",
			"Auto", "Ręcznie", "zapisanie dostępne", "bieżąca", "Nic jeszcze nie zapisano.");

		private static readonly Dictionary<K, string> _tr = Make(
			"Kaydet / Yükle", "Şimdi kaydet", "Ağaç görünümü", "Oyun",
			"Bu kaydın geçtiği sahneler ve cevapların, ardından seni bekleyenler.",
			"Kapat", "Yükle", "Sil", "Kaydet", "İptal",
			"Bu kayıt yüklensin mi?\n\n{0}\n\nÖzgün kaydınız değiştirilmeyecek.",
			"Bu kayıt listeden kaldırılsın mı?\n\n{0}\n\nDosya silinir. Oyunun kayıtlarına dokunulmaz.",
			"Oyun şimdi kaydedilsin mi?\n\nListeye yeni bir satır olarak eklenir. Oyunun kayıtlarına dokunulmaz.",
			"Henüz kayıt yok. Seçim noktaları otomatik kaydedilir; aşağıdaki «Şimdi kaydet»i kullanın.",
			"Tür", "Kayıt", "Bölüm", "Yaş", "Yıl", "Mevsim", "Tur", "Sahne", "Seçim",
			"Otomatik", "Elle", "kayıt var", "geçerli", "Henüz bir şey kaydedilmedi.");

		private static readonly Dictionary<K, string> _ja = Make(
			"保存 / 読み込み", "今すぐ保存", "ツリー表示", "プレイ記録",
			"このセーブが通ったシーンとあなたの答え、そしてこの先の展開。",
			"閉じる", "読み込む", "削除", "保存", "キャンセル",
			"このセーブを読み込みますか？\n\n{0}\n\n元のセーブは変更されません。",
			"このセーブを一覧から削除しますか？\n\n{0}\n\nファイルが削除されます。ゲームのセーブは変更されません。",
			"現在のゲームを保存しますか？\n\n一覧に新しい行として追加されます。ゲームのセーブは変更されません。",
			"まだセーブがありません。選択地点は自動で記録されます。下の「今すぐ保存」を使ってください。",
			"種類", "保存日時", "章", "年齢", "年", "季節", "ターン", "シーン", "選択",
			"自動", "手動", "セーブあり", "現在", "まだ記録がありません。");

		private static readonly Dictionary<K, string> _ko = Make(
			"저장 / 불러오기", "지금 저장", "트리 보기", "플레이 기록",
			"이 저장이 지나온 장면과 당신의 답, 그리고 앞으로의 장면.",
			"닫기", "불러오기", "삭제", "저장", "취소",
			"이 저장을 불러올까요?\n\n{0}\n\n원래 저장은 변경되지 않습니다.",
			"이 저장을 목록에서 삭제할까요?\n\n{0}\n\n파일이 삭제됩니다. 게임 저장은 건드리지 않습니다.",
			"현재 게임을 저장할까요?\n\n목록에 새 줄로 추가됩니다. 게임 저장은 건드리지 않습니다.",
			"아직 저장이 없습니다. 선택 지점은 자동으로 기록됩니다. 아래의 «지금 저장»을 사용하세요.",
			"종류", "저장 시각", "장", "나이", "연도", "계절", "턴", "장면", "선택",
			"자동", "수동", "저장 있음", "현재", "아직 기록이 없습니다.");

		private static readonly Dictionary<K, string> _zh = Make(
			"保存 / 读取", "立即保存", "树状视图", "游戏历程",
			"此存档经历过的场景及你的选择，以及接下来的内容。",
			"关闭", "读取", "删除", "保存", "取消",
			"读取此存档？\n\n{0}\n\n原始存档不会被修改。",
			"从列表中移除此存档？\n\n{0}\n\n文件将被删除。游戏存档不受影响。",
			"现在保存游戏？\n\n将作为新的一行加入列表。游戏存档不受影响。",
			"还没有存档。选择点会自动记录；请使用下方的“立即保存”。",
			"类型", "保存时间", "章节", "年龄", "年份", "季节", "回合", "场景", "选择",
			"自动", "手动", "有存档", "当前", "还没有记录。");
	}
}
