using UnityEngine;

namespace Doorstop
{
	// Every colour the mod's windows use, in one place. Lighter or see-through
	// versions of a colour are made with Fade, never as a new near-copy.
	public static class Palette
	{
		public static readonly Color Paper = new Color(0.93f, 0.89f, 0.79f);       // window background
		public static readonly Color PaperLight = new Color(0.97f, 0.95f, 0.89f);  // pop-ups, confirm box
		public static readonly Color Card = new Color(0.84f, 0.79f, 0.68f);        // answer boxes, save rows, rules
		public static readonly Color CardAlt = new Color(0.88f, 0.84f, 0.75f);     // every other save row
		public static readonly Color Highlight = new Color(1f, 0.95f, 0.68f);      // a chosen answer, a manual save
		public static readonly Color HighlightAlt = new Color(1f, 0.91f, 0.56f);   // every other manual save row
		public static readonly Color Ink = new Color(0.12f, 0.10f, 0.08f);         // text, crosses, icons on paper
		public static readonly Color TextLight = new Color(0.95f, 0.92f, 0.82f);   // text and icons on dark
		public static readonly Color Muted = new Color(0.45f, 0.40f, 0.32f);       // notes, tree lines
		public static readonly Color Dark = new Color(0.30f, 0.26f, 0.20f);        // header cells, scrollbars
		public static readonly Color Accent = new Color(0.55f, 0.30f, 0.25f);      // Load, the path, titles
		public static readonly Color Grey = new Color(0.55f, 0.53f, 0.48f);        // cancel, disabled, blocked
		public static readonly Color Red = new Color(0.85f, 0.10f, 0.10f);         // marks, delete on hover
		public static readonly Color ConflictBg = new Color(0.86f, 0.42f, 0.38f);  // an answer that cannot be taken
		public static readonly Color Good = new Color(0.18f, 0.42f, 0.18f);        // a value up, a requirement met
		public static readonly Color Bad = new Color(0.62f, 0.16f, 0.12f);         // a value down, a requirement not met
		public static readonly Color Shade = new Color(0f, 0f, 0f, 0.55f);         // the dim behind a window
		public static readonly Color Track = new Color(0f, 0f, 0f, 0.12f);         // a scrollbar's track

		public static Color Fade(Color c, float alpha)
		{
			return new Color(c.r, c.g, c.b, alpha);
		}
	}
}
