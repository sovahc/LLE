using System;
using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Utils;

namespace LLE
{
	class MyConsole
	{
		struct Segment
		{
			public string Text;
			public Color Color;
		}

		class Line
		{
			public readonly List<Segment> Segments = new List<Segment>();
			public int Length;
		}

		private static readonly List<Line> _lines = new List<Line>();
		private const int MaxLines = 100;
		private const int MaxLineWidth = 80;

		private static readonly Color textBackground = new Color(0, 0, 0, 200);

		public static bool Visible = true;

		public static void Clear()
		{
			_lines.Clear();
		}

		public static void Add(string text)
		{
			Add(text, Color.White);
		}

		public static void Add(string text, Color color)
		{
			LLE.Log(text);
			AddMultiline(text, color);
			AddMultiline("\n", color);
		}

		public static void AddNewLine()
		{   _lines.Add(new Line());
		}

		public static void AddMultiline(string chunk, Color color)
		{
			if (chunk == null) return;
			if (_lines.Count == 0) AddNewLine();

			var lines = chunk.Split('\n');
			for (int lineIndex = 0; lineIndex < lines.Length; ++lineIndex)
			{
				var ss = lines[lineIndex];

				while (ss.Length > 0)
				{
					var lastLine = _lines[_lines.Count - 1];
					int freeSpace = MaxLineWidth - lastLine.Length;
					if (freeSpace <= 0) { AddNewLine(); continue; }

					int take = Math.Min(ss.Length, freeSpace);
					lastLine.Segments.Add(new Segment { Text = ss.Substring(0, take), Color = color });
					lastLine.Length += take;
					ss = ss.Substring(take);
				}

				if (lineIndex < lines.Length - 1) AddNewLine();
			}

			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void Render(Font font)
		{
			if (!Visible) return;
			if (_lines.Count == 0) return;

			float border = 0.01f;
			float scale = 0.0002f;
			float lineStep = font.GetHeight(scale) * 1.1f;
			float y0 = 0.5f;
			float x0 = -0.99f;
			float rectangleH = _lines.Count * lineStep;
			float rectangleW = 0;

			for (int lineIndex = 0; lineIndex < _lines.Count; ++lineIndex)
			{
				var line = _lines[_lines.Count - lineIndex - 1];
				float y = y0 + lineIndex * lineStep;
				float x = x0;

				for (int sIndex = 0; sIndex < line.Segments.Count; ++sIndex)
				{
					var s = line.Segments[sIndex];
					float width = font.String(s.Text, new Vector2D(x, y), scale, s.Color);

					x += width;
				}

				float lineWidth = x - x0;

				if (lineWidth > rectangleW) rectangleW = lineWidth;

				if(y > 1) break;
			}

			var bb = font.Rectangle(new Vector2(x0 - border, y0 - border),
				new Vector2(x0 + rectangleW + border + border, y0 + rectangleH + border + border),
				MyStringId.GetOrCompute("Square"),
				Vector2.Zero, Vector2.One, textBackground);

			MyTransparentGeometry.AddBillboard(bb, false);
			Common.Call_Add_Billboards();
		}
	}
}