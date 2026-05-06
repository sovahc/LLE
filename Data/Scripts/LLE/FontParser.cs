using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LLE
{
	public class FontParser
	{
		public struct Glyph
		{
			public Vector2 offset;
			public Vector2 size;
			public float aw;
			public int sx;
			public int sy;
		}

		private const int TexSize = 1024;
		public Dictionary<char, Glyph> Characters { get; private set; } = new Dictionary<char, Glyph>();

		public bool Parse(string xmlPath)
		{
			try
			{
				using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(xmlPath))
				{
					byte[] bytes = reader.ReadBytes((int)reader.BaseStream.Length);
					string content = System.Text.Encoding.UTF8.GetString(bytes);
					ParseXml(content.Split('\n'));
				}
				return true;
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLine("FontParser Error: " + e.Message);
				return false;
			}
		}

		private void ParseXml(string[] lines)
		{
			var dict = lines.Where(l => l.Contains("<glyph ") && !l.TrimStart().StartsWith("<!--"))
				            .Select(Attrs)
				            .ToDictionary(
							        a => DecodeChar(a["ch"]),
							        a => 
							        {
								        var origin = a["origin"].Split(',');
								        var sizeParts = a["size"].Split('x');
								        int ox = int.Parse(origin[0]);
								        int oy = int.Parse(origin[1]);
								        int sx = int.Parse(sizeParts[0]);
								        int sy = int.Parse(sizeParts[1]);
								        return new Glyph
								        {
									        offset = new Vector2((float)ox / TexSize, (TexSize - oy - sy) / (float)TexSize),
									        size = new Vector2(sx / (float)TexSize, sy / (float)TexSize),
									        aw = float.Parse(a["aw"]),
									        sx = sx,
									        sy = sy
								        };
							        });
			Characters = dict;
		}

		private static Dictionary<string, string> Attrs(string line)
		{
			var d = new Dictionary<string, string>();
			for (int i = 0; ; )
			{
				int start = line.IndexOf('"', i); if (start < 0) break;
				int end = line.IndexOf('"', start + 1); if (end < 0) break;

				int kEnd = start - 1; while (kEnd >= 0 && char.IsWhiteSpace(line[kEnd])) kEnd--;
				int kStart = kEnd; 
				while (kStart > 0 && line[kStart-1] != ' ' && line[kStart-1] != '>' && line[kStart-1] != '<') kStart--;

				string key = line.Substring(kStart, kEnd - kStart);
				if (!string.IsNullOrEmpty(key)) 
				{
					var val = line.Substring(start + 1, end - start - 1)
						.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
					d[key] = val;
				}
				
				i = end + 1;
			}
			return d;
		}

		private static char DecodeChar(string text)
		{
			if (text.Length == 1) return text[0];
			throw new Exception("Unknown XML escaped character: " + text);
		}
	}
}
