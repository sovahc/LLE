using System.Text;

namespace LLE
{
	static class Tools
	{
		public class Param
		{
			public readonly string Name, Type, Description;
			public readonly string[] Values;
			public readonly bool Required;

			// Set on a list parameter: the shape of one entry.
			public readonly Param[] Fields;

			public Param(string name, string type, string description, bool required, string[] values,
				Param[] fields = null)
			{	Name = name; Type = type; Description = description; Required = required; Values = values;
				Fields = fields;
			}
		}

		public class Tool
		{
			public readonly string Name, Description;
			public readonly Param[] Params;

			public Tool(string name, string description, Param[] parameters)
			{	Name = name; Description = description; Params = parameters;
			}
		}

		private static Param Int(string name, string description, bool required = true)
		{	return new Param(name, "integer", description, required, null);
		}
		private static Param Num(string name, string description, bool required = true)
		{	return new Param(name, "number", description, required, null);
		}
		private static Param Str(string name, string description, bool required = true)
		{	return new Param(name, "string", description, required, null);
		}
		private static Param Bool(string name, string description, bool required = false)
		{	return new Param(name, "boolean", description, required, null);
		}
		private static Param Enum(string name, string description, bool required, params string[] values)
		{	return new Param(name, "string", description, required, values);
		}
		private static Param List(string name, string description, params Param[] fields)
		{	return new Param(name, "array", description, true, null, fields);
		}

		private static Param[] None = new Param[0];

		private static Param[] Ijk()
		{	return new[] { Int("i", "Grid coordinate I."), Int("j", "Grid coordinate J."), Int("k", "Grid coordinate K.") };
		}

		private static Param[] IjkOptional()
		{	return new[] { Int("i", "Grid coordinate I. Omit all three to use your own cell.", false),
							Int("j", "Grid coordinate J.", false),
							Int("k", "Grid coordinate K.", false) };
		}

		private static Param[] Join(Param[] a, params Param[] b)
		{	var r = new Param[a.Length + b.Length];
			a.CopyTo(r, 0);
			b.CopyTo(r, a.Length);
			return r;
		}

		private static readonly Param[] IjkTo = new[]
		{	Int("i2", "Grid coordinate I of the second cell."),
			Int("j2", "Grid coordinate J of the second cell."),
			Int("k2", "Grid coordinate K of the second cell."),
		};

		private static readonly string[] Facings = { "forward", "backward", "left", "right" };
		private static readonly string[] Ups = { "up", "down" };
		private static readonly string[] Ports = { "+I", "-I", "+J", "-J", "+K", "-K" };
		private static readonly string[] TubeKinds = { "square", "round", "reinforced" };

		public static readonly Tool[] All =
		{
			new Tool("pause", "Pause the bot until something happens. Must be the only call of the batch.", None),
			new Tool("restart", "Reset the context, keeping memory. Must be the only call of the batch.", None),

			new Tool("fly", "Land at a specific grid cell.",
				Join(Ijk(), Bool("headfirst", "Fly head first (use for tight spaces or unstuck)."))),
			new Tool("fly_direction", "Fly a number of cells along one of the grid axes.", new[]
			{	Enum("direction", "Which way to fly, relative to the grid.", true,
					"forward", "backward", "left", "right", "up", "down"),
				Int("n", "How many cells to cross. Positive."),
			}),
			new Tool("approach", "Fly to the interaction point of a block, from where the action below is possible.",
				Join(Ijk(), Enum("action", "What you intend to do at that block once you arrive.", true,
					"grind", "weld", "get", "put", "recharge", "enter", "place"))),

			new Tool("note", "Your notepad.", new[]
			{	Str("text", "Your plan or notes."),
			}),
			new Tool("memory", "Store a persistent key-value pair; it survives a context reset.", new[]
			{	Str("key", "Short name of what is being remembered."),
				Str("value", "What to remember."),
			}),
			new Tool("select", "Select the grid to work on. Returns its axes.", new[]
			{	Str("name", "Name of the grid, or part of it."),
			}),
			new Tool("position", "Your own coordinates on the selected grid.", None),
			new Tool("status", "Your health, hydrogen and energy.", None),
			new Tool("say", "Send a chat message to the player. Keep it extremely short.", new[]
			{	Str("message", "What to say."),
			}),
			new Tool("exit", "Leave the cockpit or seat you are in.", None),
			new Tool("overview", "List the blocks of the selected grid by category.", None),
			new Tool("integrity", "List the damaged blocks of the selected grid.", None),
			new Tool("projection", "Build status of the projection on the selected grid.", None),
			new Tool("near", "Describe the 3x3x3 cube of cells around a point.", IjkOptional()),
			new Tool("free", "List the free cells of the 3x3x3 cube around a point.", IjkOptional()),
			new Tool("inventory", "List the items you are carrying.", None),
			new Tool("inventory_block", "List the inventory of one block.", Ijk()),
			new Tool("inventories", "List every inventory on the selected grid.", None),
			new Tool("recharge_list", "List the blocks of the selected grid you can recharge at.", None),
			new Tool("search", "Search the nearby grids for an item or a block by name.", new[]
			{	Enum("kind", "What to look for.", true, "item", "block"),
				Str("query", "Part of the name to look for."),
				Int("limit", "How many results at most. Default 5.", false),
			}),
			new Tool("distance", "Distance from you to a block.", Ijk()),
			new Tool("distance_between", "Distance between two cells.", Join(Ijk(), IjkTo)),
			new Tool("points", "List the cells you can stand in to interact with a block.", Ijk()),
			new Tool("info", "Detailed information about one block: size, state, components.", Ijk()),
			new Tool("route", "Shortest conveyor path between two blocks, with the tube pieces it needs."
				+ " Either end may be a drafted block; drafted cells count as occupied.",
				Join(Ijk(), IjkTo)),
			new Tool("transfer", "Move a number of items from one block's inventory to another's.",
				Join(Ijk(), Join(IjkTo, Str("item", "Exact item name."), Num("count", "How many to move.")))),
			new Tool("transfer_all", "Move everything from one block's inventory to another's.",
				Join(Ijk(), IjkTo)),

			new Tool("draft", "Plan one block. Nothing is built; the player sees the whole draft as a projection.",
				Join(Ijk(), Str("type", "Block type name, e.g. 'Light Armor Block'."),
					Enum("facing", "Which way the block looks.", false, Facings),
					Enum("up", "Which way its top points. Only together with facing.", false, Ups))),
			new Tool("draft_conveyor", "Plan one conveyor tube piece, named by the two directions it opens onto.",
				Join(Ijk(), Enum("port1", "First opening.", true, Ports),
					Enum("port2", "Second opening, different from the first.", true, Ports),
					Enum("kind", "Tube family. Default square.", false, TubeKinds))),
			new Tool("draft_show", "List what is in the draft.", None),
			new Tool("draft_undo", "Drop the last block from the draft.", None),
			new Tool("draft_clear", "Drop the whole draft.", None),

			new Tool("grind", "Grind the block at a cell.", Ijk()),
			new Tool("weld", "Weld the block at a cell. Incremental: several trips may be needed.", Ijk()),
			new Tool("get", "Take things out of a block's inventory. Write down everything you need"
				+ " from it — one entry per item type, all in one call.",
				Join(Ijk(), List("items", "What to take. One entry per item type.",
					Str("item", "Exact item name."),
					Num("count", "How many. Omit to take all of them.", false)))),
			new Tool("put", "Put things away. Write down everything you are carrying that has to go"
				+ " into this block — one entry per item type, all in one call.",
				Join(Ijk(), List("items", "What to put in. One entry per item type.",
					Str("item", "Exact item name."),
					Num("count", "How many. Omit to put all of them.", false)))),
			new Tool("put_all_components", "Put every component you carry into a block's inventory. Use it to free up your inventory quickly.", Ijk()),
			new Tool("build", "Build every drafted block within reach.", None),
			new Tool("place", "Build one block right now, at minimum integrity.",
				Join(Ijk(), Str("type", "Block type name."),
					Enum("facing", "Which way the block looks.", false, Facings),
					Enum("up", "Which way its top points. Only together with facing.", false, Ups))),
			new Tool("place_conveyor", "Build one conveyor tube piece right now, at minimum integrity.",
				Join(Ijk(), Enum("port1", "First opening.", true, Ports),
					Enum("port2", "Second opening, different from the first.", true, Ports),
					Enum("kind", "Tube family. Default square.", false, TubeKinds))),
			new Tool("enter", "Enter the cockpit or seat at a cell.", Ijk()),
			new Tool("recharge", "Recharge from the block at a cell.", Ijk()),
		};

		public static Tool Find(string name)
		{	foreach (var t in All)
				if (t.Name == name) return t;
			return null;
		}

		private static string schema;

		public static string Schema()
		{
			if (schema != null) return schema;

			var sb = new StringBuilder("[");

			for (int t = 0; t < All.Length; ++t)
			{
				var tool = All[t];
				if (t != 0) sb.Append(',');

				sb.Append("{\"type\":\"function\",\"function\":{\"name\":");
				Json.Quoted(sb, tool.Name);
				sb.Append(",\"description\":");
				Json.Quoted(sb, tool.Description);

				sb.Append(",\"parameters\":");
				AppendObject(sb, tool.Params);
				sb.Append("}}");
			}

			sb.Append(']');
			return schema = sb.ToString();
		}

		private static void AppendObject(StringBuilder sb, Param[] parameters)
		{
			sb.Append("{\"type\":\"object\",\"properties\":{");

			for (int p = 0; p < parameters.Length; ++p)
			{
				var param = parameters[p];
				if (p != 0) sb.Append(',');

				Json.Quoted(sb, param.Name);
				sb.Append(":{\"type\":");
				Json.Quoted(sb, param.Type);
				sb.Append(",\"description\":");
				Json.Quoted(sb, param.Description);

				if (param.Values != null)
				{	sb.Append(",\"enum\":[");
					for (int v = 0; v < param.Values.Length; ++v)
					{	if (v != 0) sb.Append(',');
						Json.Quoted(sb, param.Values[v]);
					}
					sb.Append(']');
				}

				if (param.Fields != null)
				{	sb.Append(",\"items\":");
					AppendObject(sb, param.Fields);
				}

				sb.Append('}');
			}

			sb.Append("},\"required\":[");

			bool first = true;
			foreach (var param in parameters)
			{	if (!param.Required) continue;
				if (!first) sb.Append(',');
				first = false;
				Json.Quoted(sb, param.Name);
			}

			sb.Append("]}");
		}
	}
}
