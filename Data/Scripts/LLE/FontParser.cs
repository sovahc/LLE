using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LargeLanguageEngineer
{
	public class FontParser
	{
		public struct GlyphInfo
		{
			public int Bm;
			public Vector2 UVOffset;
			public Vector2 UVSize;
			public float Aw;
			public int SizeX;
			public int SizeY;
		}

		private readonly List<string> _atlasNames = new List<string>();
		private int[] _atlasSizes;
		public Dictionary<char, GlyphInfo> Characters { get; } = new Dictionary<char, GlyphInfo>();

		public bool Parse(string xmlPath)
		{
			try
			{
				using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(xmlPath))
				{
					byte[] bytes = reader.ReadBytes((int)reader.BaseStream.Length);
					string content = System.Text.Encoding.UTF8.GetString(bytes);
					MyLog.Default.WriteLine("LLE DBG: Read " + bytes.Length + " bytes from font xml.");
					ParseXml(content);
				}
				return true;
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLine("FontParser Error: " + e.Message);
				return false;
			}
		}

		private void ParseXml(string xml)
		{
			int idx = 0;
			while (true)
			{
				idx = FindTag(xml, "bitmap", idx);
				if (idx == -1) break;
				string idStr = GetAttributeValue(xml, idx, "id");
				string name = GetAttributeValue(xml, idx, "name");
				int id = int.Parse(idStr);
				while (_atlasNames.Count <= id) _atlasNames.Add(null);
				_atlasNames[id] = name;
			}
			MyLog.Default.WriteLine("LLE DBG: Parsed bitmaps=" + _atlasNames.Count);

			int maxBm = 0;
			idx = 0;
			while (true)
			{
				idx = FindTag(xml, "bitmap", idx);
				if (idx == -1) break;
				int id = int.Parse(GetAttributeValue(xml, idx, "id"));
				if (id > maxBm) maxBm = id;
			}
			_atlasSizes = new int[maxBm + 1];

			idx = 0;
			while (true)
			{
				idx = FindTag(xml, "glyph", idx);
				if (idx == -1) break;
				string chStr = GetAttributeValue(xml, idx, "ch");
				char ch = ParseHtmlChar(chStr);
				int bmId = int.Parse(GetAttributeValue(xml, idx, "bm"));

				string originVal = GetAttributeValue(xml, idx, "origin");
				string[] originParts = originVal.Split(',');
				int ox = int.Parse(originParts[0]);
				int oy = int.Parse(originParts[1]);

				string sizeVal = GetAttributeValue(xml, idx, "size");
				string[] sizeParts = sizeVal.Split('x');
				int sx = int.Parse(sizeParts[0]);
				int sy = int.Parse(sizeParts[1]);

				float aw = float.Parse(GetAttributeValue(xml, idx, "aw"));

				if (_atlasSizes[bmId] == 0) _atlasSizes[bmId] = 1024;
				int texSize = _atlasSizes[bmId];

				GlyphInfo glyph = new GlyphInfo();
				glyph.Bm = bmId;
				// Flip Y: SE textures use bottom-left origin, XNA fonts use top-left
				glyph.UVOffset = new Vector2((float)ox / texSize, (float)(texSize - oy - sy) / texSize);
				glyph.UVSize = new Vector2((float)sx / texSize, (float)sy / texSize);
				glyph.Aw = aw;
				glyph.SizeX = sx;
				glyph.SizeY = sy;
				Characters[ch] = glyph;
			}
			MyLog.Default.WriteLine("LLE DBG: Parsed glyphs=" + Characters.Count);
		}

		private static int FindTag(string xml, string tag, int startIndex)
		{
			string search = "<" + tag;
			int idx = xml.IndexOf(search, startIndex);
			while (idx != -1)
			{
				int after = idx + search.Length;
				if (after < xml.Length && (char.IsWhiteSpace(xml[after]) || xml[after] == '/' || xml[after] == '>'))
					return after;
				idx = xml.IndexOf(search, after);
			}
			return -1;
		}

		private static string GetAttributeValue(string xml, int startIndex, string attrName)
		{
			string search = " " + attrName + "=\"";
			int idx = xml.IndexOf(search, startIndex);
			if (idx == -1) return null;
			int valStart = idx + search.Length;
			int valEnd = xml.IndexOf('"', valStart);
			if (valEnd == -1) return null;
			return xml.Substring(valStart, valEnd - valStart);
		}

		private static char ParseHtmlChar(string text)
		{
			if (text.Length == 1) return text[0];
			if (text == "&amp;") return '&';
			if (text == "&quot;") return '"';
			if (text == "&apos;") return '\'';
			if (text == "&gt;") return '>';
			if (text == "&lt;") return '<';
			if (text == "&tab;") return '\t';
			if (text == "&newline;") return '\n';
			if (text == "&nbsp;") return ' ';
			throw new Exception("Unknown XML escaped character: " + text);
		}

		public List<string> AtlasNames => _atlasNames;
	}
}
