using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using I2.Loc;
using UnityEngine.SceneManagement;
using _Scripts.AMVCC.Models.Static;
using _Scripts.Helpers;
using _Scripts.KeyParams;
using _Scripts.Managers;

namespace Doorstop
{
	// A path ID: the whole game state of a save as one short line of text,
	// to copy out of one game and paste into another.
	//
	//   BSL1:<n>:<payload>:<crc>
	//
	// <payload> is n characters of base64url (A-Z a-z 0-9 - _), the raw
	// DEFLATE of a compact binary record; <crc> is the CRC-32 of the payload
	// text as 8 upper-case hex digits. A string that is not exactly this
	// shape is refused before anything is decoded.
	//
	// The record is the mod's own format, read field by field with every
	// count, length, enum and name checked. Pasted text never reaches
	// BinaryFormatter (deserialising untrusted BinaryFormatter data can run
	// code): it is applied to a copy of the live game's own state, the way
	// the game's save holds it, and only that copy is saved and loaded.
	//
	// It is short because names are not spelled out: scenes are indices into
	// the game's build scene list, description keys into its I2 term list,
	// and the fixed lists (69 key parameters, 42 characters, 78 objectives,
	// 50 timeline events, ModelParameters' int fields) are in the game's own
	// order. A schema hash over all of those names is in the record, so a
	// path ID from another game version is refused instead of misread.
	// Objectives and events are listed in full (a locked one keeps the date
	// the game left in it); dates are seconds from the previous one. The
	// hero's name and the save slot are the pasting player's own.
	public static class PathId
	{
		private const string Prefix = "BSL1";
		public const int MaxChars = 16384;
		private const int MaxRaw = 65536;
		private const int MaxString = 256;
		private const long DateEpoch = 1700000000L;

		private static readonly Regex Shape = new Regex(@"^BSL1:(\d{1,5}):([A-Za-z0-9_-]+):([0-9A-F]{8})$");
		private static readonly Regex GameDate = new Regex(@"^(\d{1,2})/(\d{1,2})/(\d{4}) (\d{2}):(\d{2}):(\d{2})$");

		// ---- tables (built once per session from the game itself) ----

		private static List<string> _scenes;
		private static Dictionary<string, int> _sceneIndex;
		private static List<string> _terms;
		private static Dictionary<string, int> _termIndex;
		private static FieldInfo[] _intFields;
		private static uint _staticHash;

		private static void Tables()
		{
			if (_scenes != null)
			{
				return;
			}
			uint hash = 2166136261u;
			_scenes = new List<string>();
			_sceneIndex = new Dictionary<string, int>();
			for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
			{
				string name = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i) ?? "");
				_scenes.Add(name);
				if (!_sceneIndex.ContainsKey(name))
				{
					_sceneIndex[name] = i;
				}
				hash = Fnv(hash, name);
			}
			_terms = new List<string>();
			try
			{
				_terms.AddRange(LocalizationManager.GetTermsList());
			}
			catch (Exception e)
			{
				ModLog.Error("PathId: the game's term list is not readable:\n" + e);
			}
			_terms.Sort(StringComparer.Ordinal);
			_termIndex = new Dictionary<string, int>();
			for (int i = 0; i < _terms.Count; i++)
			{
				if (!_termIndex.ContainsKey(_terms[i]))
				{
					_termIndex[_terms[i]] = i;
				}
				hash = Fnv(hash, _terms[i]);
			}
			List<FieldInfo> ints = new List<FieldInfo>();
			foreach (FieldInfo f in typeof(ModelParameters).GetFields(BindingFlags.Public | BindingFlags.Instance))
			{
				if (f.FieldType == typeof(int))
				{
					ints.Add(f);
				}
			}
			ints.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
			_intFields = ints.ToArray();
			foreach (FieldInfo f in _intFields)
			{
				hash = Fnv(hash, f.Name);
			}
			_staticHash = hash;
			ModLog.Info("PathId: " + _scenes.Count + " build scenes, " + _terms.Count + " terms, "
				+ _intFields.Length + " stats");
		}

		private static uint Fnv(uint hash, string s)
		{
			foreach (char c in s ?? "")
			{
				hash = (hash ^ c) * 16777619u;
			}
			return (hash ^ 0xFFu) * 16777619u;   // a separator, so "ab","c" != "a","bc"
		}

		// The schema: the static tables plus the shape of the save's fixed lists.
		private static uint Schema(SaveLoadHelper h)
		{
			uint hash = _staticHash;
			foreach (KeyChapterParameterModel p in h.ChapterParameters)
			{
				hash = Fnv(hash, p.Name);
			}
			foreach (CharacterModel c in h.CharacterModel)
			{
				hash = Fnv(hash, ((int)c.Character).ToString());
			}
			foreach (ObjectiveStatuses o in h.AllGameObjectives.Objectives)
			{
				hash = Fnv(hash, ((int)o.Name).ToString());
			}
			foreach (GameEventStatus e in h.AllGameEvents.GameEventsList)
			{
				hash = Fnv(hash, e.Name);
			}
			return hash;
		}

		// ---- encode ----

		public static string Encode(SaveLoadHelper h)
		{
			Tables();
			Writer w = new Writer();
			uint schema = Schema(h);
			for (int i = 0; i < 4; i++)
			{
				w.Byte((byte)(schema >> (8 * i)));
			}
			w.U((h.StoryMode ? 1 : 0) | (h.IronManMode ? 2 : 0) | (h.GodMode ? 4 : 0));
			w.U((int)h.SceneState);
			w.U(_intFields.Length);
			foreach (FieldInfo f in _intFields)
			{
				w.I((int)f.GetValue(h.ModelParameters));
			}
			w.I(h.Year);
			w.I(h.PreviousYear);
			w.I(h.HeroAge);
			w.I(h.Turn);
			w.I(h.InsurrectionDayStage);
			w.I(h.InsurrectionPreviousDayStage);
			w.I(h.Season);
			w.I(h.SceneInSequence);
			w.I(h.Chapter);
			w.Str(h.CurrentConsequence);
			w.Str(h.LastOpenScene);
			w.Str(h.ChapterName);
			w.U(h.ChapterParameters.Count);
			foreach (KeyChapterParameterModel p in h.ChapterParameters)
			{
				w.Str(p.Value);
			}
			w.U(h.ShowedScenesList.Count);
			foreach (ShowedScenes s in h.ShowedScenesList)
			{
				int idx;
				bool known = s.SceneName != null && _sceneIndex.TryGetValue(s.SceneName, out idx);
				idx = known ? _sceneIndex[s.SceneName] + 1 : 0;
				w.U((idx << 1) | (s.IsShowed ? 1 : 0));
				if (!known)
				{
					w.Str(s.SceneName);
				}
			}
			w.U(h.CharacterModel.Count);
			foreach (CharacterModel c in h.CharacterModel)
			{
				w.I(c.Relation);
				w.U((int)c.Status);
				w.Term(c.Description, _termIndex);
				w.Term(c.Name, _termIndex);
				w.U(c.IsUnlocked ? 1 : 0);
				w.Date(c.UnlockDate);
			}
			// every entry: a locked one keeps whatever date and year the game
			// left in it (an empty date and 1132, or leftovers from the
			// game's own assets), so a copy matches exactly
			List<ObjectiveStatuses> objectives = h.AllGameObjectives.Objectives;
			w.U(objectives.Count);
			foreach (ObjectiveStatuses o in objectives)
			{
				w.U(o.Status ? 1 : 0);
				w.Date(o.UnlockDate);
				w.I(o.UnlockYear);
			}
			List<GameEventStatus> events = h.AllGameEvents.GameEventsList;
			w.U(events.Count);
			foreach (GameEventStatus e in events)
			{
				w.U(e.Unlocked ? 1 : 0);
				w.Date(e.UnlockDate);
				w.I(e.UnlockYear);
			}
			byte[] raw = w.ToArray();
			byte[] packed;
			using (MemoryStream ms = new MemoryStream())
			{
				using (DeflateStream z = new DeflateStream(ms, CompressionMode.Compress, true))
				{
					z.Write(raw, 0, raw.Length);
				}
				packed = ms.ToArray();
			}
			string payload = Convert.ToBase64String(packed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
			string id = Prefix + ":" + payload.Length + ":" + payload + ":" + Crc32(payload).ToString("X8");
			ModLog.Info("PathId: encoded " + h.LastOpenScene + ": " + raw.Length + " bytes, " + packed.Length
				+ " packed, " + id.Length + " characters");
			return id;
		}

		// ---- decode ----

		// Applies a path ID to `into` (a copy of the live game's state) or says
		// why not. Nothing of `into` is touched unless the whole ID is valid.
		public static bool TryApply(string text, SaveLoadHelper into, out string error)
		{
			error = null;
			try
			{
				Tables();
				text = (text ?? "").Trim();
				if (text.Length == 0 || text.Length > MaxChars)
				{
					error = Loc.T(Loc.K.PidEmpty);
					return false;
				}
				Match m = Shape.Match(text);
				if (!m.Success)
				{
					error = Loc.T(Loc.K.PidShape);
					return false;
				}
				string payload = m.Groups[2].Value;
				if (int.Parse(m.Groups[1].Value) != payload.Length)
				{
					error = Loc.T(Loc.K.PidLength);
					return false;
				}
				if (Crc32(payload).ToString("X8") != m.Groups[3].Value)
				{
					error = Loc.T(Loc.K.PidCheck);
					return false;
				}
				string b64 = payload.Replace('-', '+').Replace('_', '/');
				b64 += new string('=', (4 - b64.Length % 4) % 4);
				byte[] packed = Convert.FromBase64String(b64);
				byte[] raw = Inflate(packed);
				Decoded d = Read(new Reader(raw), into);
				if (d.Error != null)
				{
					error = d.Error;
					return false;
				}
				d.ApplyTo(into);
				ModLog.Info("PathId: decoded " + into.LastOpenScene + " (" + raw.Length + " bytes)");
				return true;
			}
			catch (Exception e)
			{
				ModLog.Error("PathId: refused:\n" + e);
				error = Loc.T(Loc.K.PidDamaged);   // the log has what exactly
				return false;
			}
		}

		private static byte[] Inflate(byte[] packed)
		{
			using (MemoryStream src = new MemoryStream(packed))
			using (DeflateStream z = new DeflateStream(src, CompressionMode.Decompress))
			using (MemoryStream dst = new MemoryStream())
			{
				byte[] buf = new byte[4096];
				int n;
				while ((n = z.Read(buf, 0, buf.Length)) > 0)
				{
					dst.Write(buf, 0, n);
					if (dst.Length > MaxRaw)
					{
						throw new InvalidDataException("too large when unpacked");
					}
				}
				return dst.ToArray();
			}
		}

		// Everything read and checked first; applied only once all of it is good.
		private sealed class Decoded
		{
			public string Error;
			public bool Story, Iron, God;
			public int SceneState;
			public int[] Stats;
			public int Year, PreviousYear, HeroAge, Turn, InsDay, InsPrevDay, Season, SceneInSequence, Chapter;
			public string Consequence, LastScene, ChapterName;
			public string[] KeyValues;
			public List<ShowedScenes> Scenes = new List<ShowedScenes>();
			public List<CharacterModel> Characters = new List<CharacterModel>();
			public List<Entry> Objectives = new List<Entry>();
			public List<Entry> Events = new List<Entry>();

			public void ApplyTo(SaveLoadHelper h)
			{
				h.StoryMode = Story;
				h.IronManMode = Iron;
				h.GodMode = God;
				h.SceneState = (Assets._Scripts.AMVCC.Core.SceneStateMachine.SceneStates)SceneState;
				for (int i = 0; i < _intFields.Length; i++)
				{
					_intFields[i].SetValue(h.ModelParameters, Stats[i]);
				}
				h.Year = Year;
				h.PreviousYear = PreviousYear;
				h.HeroAge = HeroAge;
				h.Turn = Turn;
				h.InsurrectionDayStage = InsDay;
				h.InsurrectionPreviousDayStage = InsPrevDay;
				h.Season = Season;
				h.SceneInSequence = SceneInSequence;
				h.Chapter = Chapter;
				h.CurrentConsequence = Consequence;
				h.LastOpenScene = LastScene;
				h.ChapterName = ChapterName;
				for (int i = 0; i < KeyValues.Length; i++)
				{
					h.ChapterParameters[i].Value = KeyValues[i];
				}
				h.ShowedScenesList = Scenes;
				for (int i = 0; i < Characters.Count; i++)
				{
					CharacterModel to = h.CharacterModel[i], from = Characters[i];
					to.Relation = from.Relation;
					to.Status = from.Status;
					to.Description = from.Description;
					to.Name = from.Name;
					to.IsUnlocked = from.IsUnlocked;
					to.UnlockDate = from.UnlockDate;
				}
				for (int i = 0; i < Objectives.Count; i++)
				{
					ObjectiveStatuses o = h.AllGameObjectives.Objectives[i];
					o.Status = Objectives[i].On;
					o.UnlockDate = Objectives[i].Date;
					o.UnlockYear = Objectives[i].Year;
				}
				for (int i = 0; i < Events.Count; i++)
				{
					GameEventStatus e = h.AllGameEvents.GameEventsList[i];
					e.Unlocked = Events[i].On;
					e.UnlockDate = Events[i].Date;
					e.UnlockYear = Events[i].Year;
				}
			}
		}

		private struct Entry
		{
			public bool On;
			public string Date;
			public int Year;
		}

		private static Decoded Read(Reader r, SaveLoadHelper into)
		{
			Decoded d = new Decoded();
			uint schema = 0;
			for (int i = 0; i < 4; i++)
			{
				schema |= (uint)r.Byte() << (8 * i);
			}
			if (schema != Schema(into))
			{
				d.Error = Loc.T(Loc.K.PidVersion);
				return d;
			}
			int flags = r.U(7);
			d.Story = (flags & 1) != 0;
			d.Iron = (flags & 2) != 0;
			d.God = (flags & 4) != 0;
			d.SceneState = r.U(5);
			if (!Enum.IsDefined(typeof(Assets._Scripts.AMVCC.Core.SceneStateMachine.SceneStates), d.SceneState))
			{
				throw new InvalidDataException("scene state");
			}
			if (r.U(_intFields.Length) != _intFields.Length)
			{
				throw new InvalidDataException("stat count");
			}
			d.Stats = new int[_intFields.Length];
			for (int i = 0; i < d.Stats.Length; i++)
			{
				d.Stats[i] = r.I(-100000, 100000);
			}
			d.Year = r.I(0, 5000);
			d.PreviousYear = r.I(0, 5000);
			d.HeroAge = r.I(0, 200);
			d.Turn = r.I(-1000, 1000);
			d.InsDay = r.I(-1000, 1000);
			d.InsPrevDay = r.I(-1000, 1000);
			d.Season = r.I(-10, 10);
			d.SceneInSequence = r.I(-1000, 1000);
			d.Chapter = r.I(0, 10);
			d.Consequence = r.Str(true);
			d.LastScene = r.Str(true);
			if (string.IsNullOrEmpty(d.LastScene) || !_sceneIndex.ContainsKey(d.LastScene))
			{
				throw new InvalidDataException("the scene is not one of the game's");
			}
			d.ChapterName = r.Str(true);
			int keys = r.U(into.ChapterParameters.Count);
			if (keys != into.ChapterParameters.Count)
			{
				throw new InvalidDataException("key parameter count");
			}
			d.KeyValues = new string[keys];
			for (int i = 0; i < keys; i++)
			{
				d.KeyValues[i] = r.Str(true);
			}
			int scenes = r.U(5000);
			for (int i = 0; i < scenes; i++)
			{
				int v = r.U(int.MaxValue);
				int idx = v >> 1;
				string name;
				if (idx == 0)
				{
					name = r.Str(false);
				}
				else if (idx - 1 < _scenes.Count)
				{
					name = _scenes[idx - 1];
				}
				else
				{
					throw new InvalidDataException("scene index");
				}
				d.Scenes.Add(new ShowedScenes { SceneName = name, IsShowed = (v & 1) != 0 });
			}
			int chars = r.U(into.CharacterModel.Count);
			if (chars != into.CharacterModel.Count)
			{
				throw new InvalidDataException("character count");
			}
			for (int i = 0; i < chars; i++)
			{
				CharacterModel c = new CharacterModel();
				c.Relation = r.I(-100, 100);
				int status = r.U(100000);
				if (!Enum.IsDefined(typeof(CharacterStatus), status))
				{
					throw new InvalidDataException("character status");
				}
				c.Status = (CharacterStatus)status;
				c.Description = r.Term(_terms);
				c.Name = r.Term(_terms);
				c.IsUnlocked = r.U(1) == 1;
				c.UnlockDate = r.Date();
				d.Characters.Add(c);
			}
			ReadEntries(r, d.Objectives, into.AllGameObjectives.Objectives.Count);
			ReadEntries(r, d.Events, into.AllGameEvents.GameEventsList.Count);
			if (!r.AtEnd)
			{
				throw new InvalidDataException("data after the end");
			}
			return d;
		}

		private static void ReadEntries(Reader r, List<Entry> into, int total)
		{
			if (r.U(total) != total)
			{
				throw new InvalidDataException("list length");
			}
			for (int i = 0; i < total; i++)
			{
				Entry e = new Entry();
				e.On = r.U(1) == 1;
				e.Date = r.Date();
				e.Year = r.I(-10000, 10000);
				into.Add(e);
			}
		}

		// ---- the record's primitives ----

		private sealed class Writer
		{
			private readonly MemoryStream _ms = new MemoryStream();
			private long _lastDate = DateEpoch;

			public void Byte(byte b)
			{
				_ms.WriteByte(b);
			}

			public void U(int v)
			{
				uint u = (uint)v;
				while (u >= 0x80)
				{
					_ms.WriteByte((byte)(u | 0x80));
					u >>= 7;
				}
				_ms.WriteByte((byte)u);
			}

			public void I(int v)
			{
				U((int)((uint)(v << 1) ^ (uint)(v >> 31)));   // zigzag: small negatives stay short
			}

			public void L(long v)
			{
				ulong u = (ulong)((v << 1) ^ (v >> 63));
				while (u >= 0x80)
				{
					_ms.WriteByte((byte)(u | 0x80));
					u >>= 7;
				}
				_ms.WriteByte((byte)u);
			}

			// 0 = null, else length + 1, then UTF-8
			public void Str(string s)
			{
				if (s == null)
				{
					U(0);
					return;
				}
				byte[] b = Encoding.UTF8.GetBytes(s);
				U(b.Length + 1);
				_ms.Write(b, 0, b.Length);
			}

			// 0 = null, 1 = spelled out, n + 2 = the game's term n
			public void Term(string s, Dictionary<string, int> table)
			{
				int idx;
				if (s == null)
				{
					U(0);
				}
				else if (table.TryGetValue(s, out idx))
				{
					U(idx + 2);
				}
				else
				{
					U(1);
					Str(s);
				}
			}

			// 0 = null, 1 = the game's "M/d/yyyy HH:mm:ss" as seconds from the
			// previous date, 2 = spelled out (any other form, kept exactly),
			// 3 = empty
			public void Date(string s)
			{
				long secs;
				if (s == null)
				{
					U(0);
				}
				else if (s.Length == 0)
				{
					U(3);
				}
				else if (TryParseDate(s, out secs))
				{
					U(1);
					L(secs - _lastDate);
					_lastDate = secs;
				}
				else
				{
					U(2);
					Str(s);
				}
			}

			public byte[] ToArray()
			{
				return _ms.ToArray();
			}
		}

		private sealed class Reader
		{
			private readonly byte[] _b;
			private int _at;
			private long _lastDate = DateEpoch;

			public Reader(byte[] b)
			{
				_b = b;
			}

			public bool AtEnd
			{
				get { return _at == _b.Length; }
			}

			public byte Byte()
			{
				if (_at >= _b.Length)
				{
					throw new InvalidDataException("cut off");
				}
				return _b[_at++];
			}

			private ulong Raw()
			{
				ulong v = 0;
				for (int shift = 0; shift < 64; shift += 7)
				{
					byte b = Byte();
					v |= (ulong)(b & 0x7F) << shift;
					if ((b & 0x80) == 0)
					{
						return v;
					}
				}
				throw new InvalidDataException("number too long");
			}

			// an unsigned number no larger than `max`
			public int U(int max)
			{
				ulong v = Raw();
				if (v > (ulong)max)
				{
					throw new InvalidDataException("number out of range");
				}
				return (int)v;
			}

			public int I(int min, int max)
			{
				ulong u = Raw();
				long v = (long)(u >> 1) ^ -(long)(u & 1);
				if (v < min || v > max)
				{
					throw new InvalidDataException("number out of range");
				}
				return (int)v;
			}

			public long L()
			{
				ulong u = Raw();
				return (long)(u >> 1) ^ -(long)(u & 1);
			}

			// printable text only, at most MaxString bytes
			public string Str(bool allowNull)
			{
				int n = U(MaxString + 1);
				if (n == 0)
				{
					if (!allowNull)
					{
						throw new InvalidDataException("missing text");
					}
					return null;
				}
				n--;
				if (_at + n > _b.Length)
				{
					throw new InvalidDataException("cut off");
				}
				string s = new UTF8Encoding(false, true).GetString(_b, _at, n);
				_at += n;
				foreach (char c in s)
				{
					if (char.IsControl(c) || c == '<' || c == '>')
					{
						throw new InvalidDataException("text with control or markup characters");
					}
				}
				return s;
			}

			public string Term(List<string> table)
			{
				int v = U(table.Count + 1);
				if (v == 0)
				{
					return null;
				}
				return v == 1 ? Str(false) : table[v - 2];
			}

			public string Date()
			{
				int kind = U(3);
				if (kind == 0)
				{
					return null;
				}
				if (kind == 3)
				{
					return "";
				}
				if (kind == 2)
				{
					return Str(false);
				}
				long secs = _lastDate + L();
				if (secs < 0 || secs > 32503680000L)   // before 1970 or after 3000
				{
					throw new InvalidDataException("date");
				}
				_lastDate = secs;
				return FormatDate(secs);
			}
		}

		private static bool TryParseDate(string s, out long secs)
		{
			secs = 0;
			Match m = GameDate.Match(s);
			if (!m.Success)
			{
				return false;
			}
			try
			{
				DateTime t = new DateTime(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[1].Value),
					int.Parse(m.Groups[2].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value),
					int.Parse(m.Groups[6].Value));
				secs = (long)(t - new DateTime(1970, 1, 1)).TotalSeconds;
				return FormatDate(secs) == s;   // kept exactly, or spelled out
			}
			catch (ArgumentOutOfRangeException)
			{
				return false;
			}
		}

		private static string FormatDate(long secs)
		{
			DateTime t = new DateTime(1970, 1, 1).AddSeconds(secs);
			return t.Month + "/" + t.Day + "/" + t.Year + " " + t.Hour.ToString("00") + ":"
				+ t.Minute.ToString("00") + ":" + t.Second.ToString("00");
		}

		private static uint[] _crcTable;

		private static uint Crc32(string text)
		{
			if (_crcTable == null)
			{
				_crcTable = new uint[256];
				for (uint i = 0; i < 256; i++)
				{
					uint c = i;
					for (int k = 0; k < 8; k++)
					{
						c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
					}
					_crcTable[i] = c;
				}
			}
			uint crc = 0xFFFFFFFFu;
			foreach (char ch in text)
			{
				crc = _crcTable[(crc ^ (byte)ch) & 0xFF] ^ (crc >> 8);
			}
			return crc ^ 0xFFFFFFFFu;
		}
	}
}
