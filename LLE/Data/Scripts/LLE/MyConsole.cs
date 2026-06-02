using System.Collections.Generic;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace LLE
{
    class MyConsole
	{
		struct LineData
		{
			public string Text;
			public Color Color;
		}

		private static readonly List<LineData> _lines = new List<LineData>();
		private const int MaxLines = 100;
		private const int MaxLineWidth = 80;

		private static readonly Color textBackground = new Color(0, 0, 0, 200);

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
			Utilities.Log(text);
			AddMultiline(text, color);
			AddMultiline("\n", color);
		}

		public static void AddNewLine(Color color)
		{   _lines.Add(new LineData { Text = "", Color = color });
		}

		public static void AddMultiline(string chunk, Color color)
		{
			if (chunk == null) return;

			if(_lines.Count == 0) AddNewLine(color);

			do
			{	var lastLineText = _lines[_lines.Count-1].Text;
				var freeSpace = MaxLineWidth - lastLineText.Length;

				if(freeSpace <= 0) { AddNewLine(color); continue; }

				var newLine = chunk.IndexOf('\n');
				var end = newLine >= 0 ? newLine : chunk.Length;

				if (end > freeSpace) end = freeSpace;

				lastLineText += chunk.Substring(0, end);

				_lines[_lines.Count-1] = new LineData { Text = lastLineText, Color = color };

				if(newLine >= 0)
				{   ++end;
					AddNewLine(color);
				}
				chunk = chunk.Substring(end);
			} while(chunk.Length != 0);

			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void Render(Font font)
		{
			if (_lines.Count == 0) return;

			float border = 0.01f;
			float scale = 0.0007f;
			float lineStep = font.GetHeight(scale) * 1.2f;
			float y0 = 0;
			float x0 = -0.99f;
			float rectangleH = _lines.Count * lineStep;
			float rectangleW = 0;

			for (int i = 0; i < _lines.Count; ++i)
			{
				var line = _lines[_lines.Count - i - 1];
				float y = y0 + i * lineStep;
				var w = font.String(line.Text, new Vector2D(x0, y), scale, line.Color);
				if (w > rectangleW) rectangleW = w;

				if(y < 0) break;
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