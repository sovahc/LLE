using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LLE
{
	enum JsonKind { Null, Bool, Number, String, Array, Object }

	class Json
	{
		public JsonKind Kind;
		public bool Bool;
		public double Number;
		public string String;
		public List<Json> Array;
		public Dictionary<string, Json> Object;

		public string Raw;

		public bool Is(JsonKind k) { return Kind == k; }

		public Json Field(string key)
		{
			Json v;
			if (Kind != JsonKind.Object || !Object.TryGetValue(key, out v)) return null;
			return v;
		}

		public static void Quoted(StringBuilder sb, string s)
		{
			sb.Append('"');
			foreach (var c in s)
			{
				if (c == '"' || c == '\\') sb.Append('\\').Append(c);
				else if (c == '\n') sb.Append("\\n");
				else if (c == '\r') sb.Append("\\r");
				else if (c == '\t') sb.Append("\\t");
				else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
				else sb.Append(c);
			}
			sb.Append('"');
		}

		public static string Unescape(string inner, out string error)
		{
			var v = Parse("\"" + inner + "\"", out error);
			return v == null || !v.Is(JsonKind.String) ? null : v.String;
		}

		public static Json Parse(string text, out string error)
		{
			var p = new Parser(text);
			var v = p.ParseValue(out error);
			if (v == null) return null;

			p.SkipWhitespace();
			if (!p.End)
			{	error = $"unexpected text after the value at character {p.Position}";
				return null;
			}
			return v;
		}

		private class Parser
		{
			private readonly string s;
			private int i;

			public Parser(string text) { s = text; }

			public int Position { get { return i; } }
			public bool End { get { return i >= s.Length; } }

			public void SkipWhitespace()
			{	while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) ++i;
			}

			public Json ParseValue(out string error)
			{
				SkipWhitespace();
				int from = i;

				var value = ParseValueBody(out error);
				if (value != null) value.Raw = s.Substring(from, i - from);
				return value;
			}

			private Json ParseValueBody(out string error)
			{
				error = null;
				SkipWhitespace();

				if (End)
				{	error = "the text is empty";
					return null;
				}

				char c = s[i];

				if (c == '{') return ParseObject(out error);
				if (c == '[') return ParseArray(out error);
				if (c == '"' || c == '\'')
				{	// A single-quoted string is not JSON, but a model that writes one means a string.
					string str = ParseString(out error);
					if (str == null) return null;
					return new Json { Kind = JsonKind.String, String = str };
				}

				if (Literal("true"))  return new Json { Kind = JsonKind.Bool, Bool = true };
				if (Literal("false")) return new Json { Kind = JsonKind.Bool, Bool = false };
				if (Literal("null"))  return new Json { Kind = JsonKind.Null };

				return ParseNumber(out error);
			}

			private bool Literal(string word)
			{
				if (i + word.Length > s.Length) return false;
				if (string.Compare(s, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
				i += word.Length;
				return true;
			}

			private Json ParseObject(out string error)
			{
				error = null;
				++i;

				var map = new Dictionary<string, Json>(StringComparer.OrdinalIgnoreCase);

				while (true)
				{
					SkipWhitespace();
					if (End)
					{	error = "the object is never closed with '}'";
						return null;
					}
					if (s[i] == '}') { ++i; break; }

					string key = ParseString(out error);
					if (key == null)
					{	if (error == null) error = $"expected a quoted field name at character {i}";
						return null;
					}

					SkipWhitespace();
					if (End || s[i] != ':')
					{	error = $"expected ':' after field '{key}'";
						return null;
					}
					++i;

					var value = ParseValue(out error);
					if (value == null) return null;

					map[key] = value;

					SkipWhitespace();
					if (!End && s[i] == ',') { ++i; continue; }
				}

				return new Json { Kind = JsonKind.Object, Object = map };
			}

			private Json ParseArray(out string error)
			{
				error = null;
				++i;

				var list = new List<Json>();

				while (true)
				{
					SkipWhitespace();
					if (End)
					{	error = "the array is never closed with ']'";
						return null;
					}
					if (s[i] == ']') { ++i; break; }

					var value = ParseValue(out error);
					if (value == null) return null;

					list.Add(value);

					SkipWhitespace();
					if (!End && s[i] == ',') { ++i; continue; }
				}

				return new Json { Kind = JsonKind.Array, Array = list };
			}

			private string ParseString(out string error)
			{
				error = null;
				SkipWhitespace();

				if (End || (s[i] != '"' && s[i] != '\'')) return null;

				char quote = s[i];
				++i;

				var sb = new StringBuilder();

				while (i < s.Length && s[i] != quote)
				{
					char c = s[i++];

					if (c != '\\') { sb.Append(c); continue; }

					if (i >= s.Length) break;

					char e = s[i++];
					switch (e)
					{
						case 'n': sb.Append('\n'); break;
						case 't': sb.Append('\t'); break;
						case 'r': sb.Append('\r'); break;
						case 'b': sb.Append('\b'); break;
						case 'f': sb.Append('\f'); break;
						case 'u':
							if (i + 4 > s.Length)
							{	error = "a \\u escape is cut short";
								return null;
							}
							int code;
							if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
								CultureInfo.InvariantCulture, out code))
							{	error = $"'{s.Substring(i, 4)}' is not a hex code after \\u";
								return null;
							}
							sb.Append((char)code);
							i += 4;
							break;
						default: sb.Append(e); break;
					}
				}

				if (i >= s.Length)
				{	error = "the string is never closed";
					return null;
				}
				++i;

				return sb.ToString();
			}

			private Json ParseNumber(out string error)
			{
				error = null;
				int start = i;

				if (i < s.Length && (s[i] == '-' || s[i] == '+')) ++i;
				while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.'
					|| s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+')) ++i;

				double d;
				if (i == start || !double.TryParse(s.Substring(start, i - start),
					NumberStyles.Float, CultureInfo.InvariantCulture, out d))
				{	error = $"'{Cut(s, start)}' is not a value";
					return null;
				}

				return new Json { Kind = JsonKind.Number, Number = d };
			}

			private static string Cut(string text, int at)
			{
				int n = Math.Min(12, text.Length - at);
				return n <= 0 ? "" : text.Substring(at, n);
			}
		}
	}
}
