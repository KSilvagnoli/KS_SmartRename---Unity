// KS_SmartRename.cs
// ─────────────────────────────────────────────────────────────────────────────
// Smart Rename — bulk search/replace renaming for Unity assets and GameObjects.
//
// Features:
//   • Works on Project window selection (assets) and Hierarchy selection (GameObjects)
//   • Right-click context menus: Assets > KS Smart Rename, GameObject > KS Smart Rename
//   • Live two-column preview with per-item checkboxes
//   • Type filter bar — dynamically built from selection (.anim, .prefab, GO, etc.)
//   • Operation stack — chain multiple rename passes in order:
//       - Search / Replace  (plain string, regex, whole-word, case-sensitive)
//       - Add Prefix
//       - Add Suffix
//       - Number  (append sequential number, configurable start, step, padding, separator)
//   • Each operation can be toggled on/off independently
//   • Enter key in search/replace fields triggers Apply when ready
//   • Folder support (renames via AssetDatabase, preserves references)
//   • Optional Addressables address sync (reflection-based, no project settings needed)
//   • Safe: uses AssetDatabase.RenameAsset for assets (preserves GUIDs/references)
//           uses Undo.RecordObject for scene GameObjects (fully undoable)
//   • Warns before applying to assets (no undo)
//   • Styles rebuilt on domain reload — no stale GUIStyle after recompile
//
// Menu: Tools > KS > Smart Rename
// ─────────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace KS.Tools
{
	public class KS_SmartRename : EditorWindow
	{
		// ─────────────────────────────────────────────────────────────
		// Constants
		// ─────────────────────────────────────────────────────────────
		private const string WINDOW_TITLE = "KS Smart Rename";
		private const float PREVIEW_ROW_H = 20f;
		private const float COL_CHECKBOX_W = 20f;
		private const float COL_ICON_W = 20f;
		private const float COL_TYPE_W = 52f;
		private const float HEADER_H = 18f;

		// ─────────────────────────────────────────────────────────────
		// Menu — Tools bar
		// ─────────────────────────────────────────────────────────────
		[MenuItem("Tools/KS/Smart Rename")]
		public static void Open()
		{
			var w = GetWindow<KS_SmartRename>(WINDOW_TITLE);
			w.minSize = new Vector2(580, 560);
			w.Show();
		}

		// ─────────────────────────────────────────────────────────────
		// Menu — Right-click in Project window
		// ─────────────────────────────────────────────────────────────
		[MenuItem("Assets/KS Smart Rename", false, 19)]
		private static void OpenFromAssets()
		{
			var w = GetWindow<KS_SmartRename>(WINDOW_TITLE);
			w.minSize = new Vector2(580, 560);
			w.Show();
		}

		[MenuItem("Assets/KS Smart Rename", true)]
		private static bool OpenFromAssetsValidate() => Selection.objects.Length > 0;

		// ─────────────────────────────────────────────────────────────
		// Menu — Right-click in Hierarchy
		// ─────────────────────────────────────────────────────────────
		[MenuItem("GameObject/KS Smart Rename", false, 49)]
		private static void OpenFromHierarchy()
		{
			var w = GetWindow<KS_SmartRename>(WINDOW_TITLE);
			w.minSize = new Vector2(580, 560);
			w.Show();
		}

		[MenuItem("GameObject/KS Smart Rename", true)]
		private static bool OpenFromHierarchyValidate() => Selection.gameObjects.Length > 0;

		// ─────────────────────────────────────────────────────────────
		// Data types
		// ─────────────────────────────────────────────────────────────
		private enum ItemKind { Asset, Folder, GameObject }

		private class RenameItem
		{
			public ItemKind Kind;
			public UnityEngine.Object Target;
			public string OriginalName;
			public string PreviewName;
			public string AssetPath;
			public string Extension;    // lowercase e.g. ".anim"
			public bool Selected = true;
			public bool WillChange => OriginalName != PreviewName;
			public bool HasError;
			public string ErrorMessage;
			// Character ranges in PreviewName added/changed by ops — (startIndex, length).
			// Built during RebuildPreviews so DrawDiffName reads exact op contributions.
			public readonly List<(int start, int length)> HighlightRanges = new List<(int, int)>();
		}

		// ─────────────────────────────────────────────────────────────
		// Operation Stack
		// ─────────────────────────────────────────────────────────────
		private enum OpType { SearchReplace, Prefix, Suffix, Number }

		[Serializable]
		private class RenameOp
		{
			public OpType OpType = OpType.SearchReplace;
			public bool Enabled = true;

			// Search / Replace
			public string SearchText = string.Empty;
			public string ReplaceText = string.Empty;
			public bool CaseSensitive = false;
			public bool UseRegex = false;
			public bool MatchWholeWord = false;

			// Prefix / Suffix
			public string PrefixText = string.Empty;
			public string SuffixText = string.Empty;

			// Number
			public int NumberStart = 1;
			public int NumberStep = 1;
			public int NumberPadding = 2;       // minimum digit width, zero-padded
			public string NumberSep = "_";     // separator between name and number
			public bool NumberPrefix = false;   // true = prepend, false = append

			// Runtime cache
			[NonSerialized] public string RegexError = null;
			[NonSerialized] public Regex CompiledRegex = null;
		}

		private readonly List<RenameOp> _ops = new List<RenameOp>();

		// ─────────────────────────────────────────────────────────────
		// State — Options
		// ─────────────────────────────────────────────────────────────
		private bool _syncAddressables = false;

		// ─────────────────────────────────────────────────────────────
		// State — Type filter
		// ─────────────────────────────────────────────────────────────
		private string _activeFilter = null;
		private string[] _availableFilters = Array.Empty<string>();

		// ─────────────────────────────────────────────────────────────
		// State — Lists
		// ─────────────────────────────────────────────────────────────
		private readonly List<RenameItem> _items = new List<RenameItem>();
		private readonly List<RenameItem> _visibleItems = new List<RenameItem>();
		private Vector2 _scrollPos;
		private Vector2 _opsScrollPos;

		// ─────────────────────────────────────────────────────────────
		// State — UI flags
		// ─────────────────────────────────────────────────────────────
		private bool _previewDirty = true;
		private bool _filterDirty = true;
		private float _opsAreaHeight = 0f;  // measured each frame, drives preview list height

		// ─────────────────────────────────────────────────────────────
		// Styles
		// ─────────────────────────────────────────────────────────────
		private GUIStyle _styleOriginal;
		private GUIStyle _styleChanged;
		private GUIStyle _styleUnchanged;
		private GUIStyle _styleError;
		private GUIStyle _styleHeader;
		private GUIStyle _styleFilterBtn;
		private GUIStyle _styleFilterBtnActive;
		private GUIStyle _styleOpBox;
		private GUIStyle _styleHighlight;  // changed characters inside the renamed name
		private bool _stylesReady = false;

		// ─────────────────────────────────────────────────────────────
		// Addressables — reflection cache
		// ─────────────────────────────────────────────────────────────
		private static bool _addressablesChecked = false;
		private static bool _addressablesAvailable = false;
		private static Type _typeDefaultObject;
		private static Type _typeSettings;
		private static PropertyInfo _propDefaultSettings;
		private static PropertyInfo _propGroups;
		private static MethodInfo _methodGetEntry;
		private static PropertyInfo _propAddress;
		private static MethodInfo _methodSetAddress;
		private static MethodInfo _methodSetDirty;

		// ─────────────────────────────────────────────────────────────
		// Unity Events
		// ─────────────────────────────────────────────────────────────
		private void OnEnable()
		{
			_stylesReady = false;
			EnsureAddressablesReflection();
			Selection.selectionChanged += OnSelectionChanged;

			// Seed with one Search/Replace op if stack is empty
			if (_ops.Count == 0)
				_ops.Add(new RenameOp { OpType = OpType.SearchReplace });

			RefreshItemsFromSelection();
		}

		private void OnDisable()
		{
			Selection.selectionChanged -= OnSelectionChanged;
		}

		private void OnSelectionChanged()
		{
			RefreshItemsFromSelection();
			_previewDirty = true;
			_filterDirty = true;
			Repaint();
		}

		// ─────────────────────────────────────────────────────────────
		// GUI Root
		// ─────────────────────────────────────────────────────────────
		private void OnGUI()
		{
			EnsureStyles();
			HandleKeyboard();

			EditorGUILayout.Space(6);
			DrawHeader();
			EditorGUILayout.Space(4);
			DrawOperationsSection();
			// Capture the actual rendered height of the ops section after layout
			if (Event.current.type == EventType.Repaint)
				_opsAreaHeight = GUILayoutUtility.GetLastRect().yMax;
			EditorGUILayout.Space(4);
			DrawOptionsSection();
			EditorGUILayout.Space(4);
			DrawTypeFilterBar();
			EditorGUILayout.Space(4);
			DrawPreviewHeader();
			DrawPreviewList();
			EditorGUILayout.Space(4);
			DrawFooter();
		}

		// ─────────────────────────────────────────────────────────────
		// Keyboard — Enter in any search/replace field triggers Apply
		// ─────────────────────────────────────────────────────────────
		private void HandleKeyboard()
		{
			var e = Event.current;
			if (e.type != EventType.KeyDown) return;
			if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;

			string focused = GUI.GetNameOfFocusedControl();
			if (!focused.StartsWith("KSOp")) return;  // all op fields are named KSOp_*

			int readyCount = 0;
			foreach (var item in _visibleItems)
				if (item.Selected && item.WillChange && !item.HasError) readyCount++;

			if (readyCount > 0 && HasAnyActiveOp())
			{
				TryApply();
				e.Use();
			}
		}

		private bool HasAnyActiveOp()
		{
			foreach (var op in _ops)
				if (op.Enabled) return true;
			return false;
		}

		// ─────────────────────────────────────────────────────────────
		// Section — Header title
		// ─────────────────────────────────────────────────────────────
		private void DrawHeader()
		{
			EditorGUILayout.LabelField("KS Smart Rename", EditorStyles.boldLabel);
			DrawSeparator();
		}

		// ─────────────────────────────────────────────────────────────
		// Section — Operation Stack
		// ─────────────────────────────────────────────────────────────
		private void DrawOperationsSection()
		{
			// Stack header + Add button
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Operations", EditorStyles.boldLabel, GUILayout.Width(90));
			GUILayout.FlexibleSpace();

			if (GUILayout.Button(new GUIContent("+ Search/Replace", "Add a Search / Replace operation. Finds a string in each name and replaces it. Supports plain text, regex, case-sensitive, and whole-word matching."), EditorStyles.miniButton, GUILayout.Width(120)))
			{ _ops.Add(new RenameOp { OpType = OpType.SearchReplace }); _previewDirty = true; }

			if (GUILayout.Button(new GUIContent("+ Prefix", "Add a Prefix operation. Prepends text to the beginning of each name."), EditorStyles.miniButton, GUILayout.Width(70)))
			{ _ops.Add(new RenameOp { OpType = OpType.Prefix }); _previewDirty = true; }

			if (GUILayout.Button(new GUIContent("+ Suffix", "Add a Suffix operation. Appends text to the end of each name."), EditorStyles.miniButton, GUILayout.Width(70)))
			{ _ops.Add(new RenameOp { OpType = OpType.Suffix }); _previewDirty = true; }

			if (GUILayout.Button(new GUIContent("+ Number", "Add a Number operation. Appends or prepends a sequential number to each name. Configurable start, step, padding, and separator."), EditorStyles.miniButton, GUILayout.Width(70)))
			{ _ops.Add(new RenameOp { OpType = OpType.Number }); _previewDirty = true; }

			GUILayout.Space(8);

			// Reset clears to a single fresh Search/Replace op
			if (GUILayout.Button(new GUIContent("Reset", "Clears all operations and starts fresh with a single Search / Replace operation."), EditorStyles.miniButton, GUILayout.Width(46)))
			{
				_ops.Clear();
				_ops.Add(new RenameOp { OpType = OpType.SearchReplace });
				_previewDirty = true;
			}

			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(2);

			// Draw each op
			int removeIdx = -1;
			int moveUp = -1;
			int moveDown = -1;

			for (int i = 0; i < _ops.Count; i++)
			{
				var op = _ops[i];
				bool changed = DrawOp(op, i, _ops.Count, out bool wantsRemove, out bool wantsUp, out bool wantsDown);
				if (changed) _previewDirty = true;
				if (wantsRemove) removeIdx = i;
				if (wantsUp) moveUp = i;
				if (wantsDown) moveDown = i;
			}

			if (removeIdx >= 0) { _ops.RemoveAt(removeIdx); _previewDirty = true; }
			if (moveUp > 0) { SwapOps(moveUp, moveUp - 1); _previewDirty = true; }
			if (moveDown >= 0 && moveDown < _ops.Count - 1) { SwapOps(moveDown, moveDown + 1); _previewDirty = true; }
		}

		private void SwapOps(int a, int b)
		{
			var tmp = _ops[a]; _ops[a] = _ops[b]; _ops[b] = tmp;
		}

		/// <summary>Draws a single operation card. Returns true if any field changed.</summary>
		private bool DrawOp(RenameOp op, int idx, int total,
			out bool wantsRemove, out bool wantsUp, out bool wantsDown)
		{
			wantsRemove = false;
			wantsUp = false;
			wantsDown = false;
			bool changed = false;

			EditorGUI.BeginChangeCheck();

			// Card background
			var boxRect = EditorGUILayout.BeginVertical(_styleOpBox);
			EditorGUI.DrawRect(boxRect, new Color(0f, 0f, 0f, op.Enabled ? 0.12f : 0.04f));

			// ── Op header row ────────────────────────────────────────
			EditorGUILayout.BeginHorizontal();

			// Enable toggle + label
			bool wasEnabled = op.Enabled;
			op.Enabled = EditorGUILayout.ToggleLeft(new GUIContent(OpLabel(op), "Toggle this operation on or off. Disabled operations are skipped but not removed from the stack."), op.Enabled, GUILayout.Width(160));
			if (op.Enabled != wasEnabled) changed = true;

			GUILayout.FlexibleSpace();

			// Move up / down / remove buttons
			using (new EditorGUI.DisabledScope(idx == 0))
				if (GUILayout.Button(new GUIContent("▲", "Move this operation up in the stack. Operations run top to bottom."), EditorStyles.miniButton, GUILayout.Width(22))) wantsUp = true;
			using (new EditorGUI.DisabledScope(idx == total - 1))
				if (GUILayout.Button(new GUIContent("▼", "Move this operation down in the stack. Operations run top to bottom."), EditorStyles.miniButton, GUILayout.Width(22))) wantsDown = true;
			if (GUILayout.Button(new GUIContent("✕", "Remove this operation from the stack."), EditorStyles.miniButton, GUILayout.Width(22))) wantsRemove = true;

			EditorGUILayout.EndHorizontal();

			// ── Op body (only when enabled) ──────────────────────────
			if (op.Enabled)
			{
				EditorGUI.indentLevel++;
				switch (op.OpType)
				{
					case OpType.SearchReplace: DrawOpSearchReplace(op, idx); break;
					case OpType.Prefix: DrawOpPrefix(op, idx); break;
					case OpType.Suffix: DrawOpSuffix(op, idx); break;
					case OpType.Number: DrawOpNumber(op, idx); break;
				}
				EditorGUI.indentLevel--;
			}

			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(2);

			if (EditorGUI.EndChangeCheck()) changed = true;
			return changed;
		}

		private static string OpLabel(RenameOp op) => op.OpType switch
		{
			OpType.SearchReplace => "Search / Replace",
			OpType.Prefix => "Add Prefix",
			OpType.Suffix => "Add Suffix",
			OpType.Number => "Number",
			_ => op.OpType.ToString()
		};

		private void DrawOpSearchReplace(RenameOp op, int idx)
		{
			// Search
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(new GUIContent("Search for", "The text to find in each item name. Plain text by default. Enable Regex for pattern matching."), GUILayout.Width(90));
			GUI.SetNextControlName($"KSOp_{idx}_search");
			op.SearchText = EditorGUILayout.TextField(op.SearchText);
			EditorGUILayout.EndHorizontal();

			// Replace
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(new GUIContent("Replace with", "The text to substitute in place of the matched string. Leave blank to delete the matched text entirely."), GUILayout.Width(90));
			GUI.SetNextControlName($"KSOp_{idx}_replace");
			op.ReplaceText = EditorGUILayout.TextField(op.ReplaceText);
			EditorGUILayout.EndHorizontal();

			// Toggles
			EditorGUILayout.BeginHorizontal();
			op.UseRegex = GUILayout.Toggle(op.UseRegex, new GUIContent("Regex",
				"Treat the Search field as a regular expression pattern instead of plain text.\n\n" +
				"Common patterns:\n" +
				"^        Start of name         ^MTX_ matches only if name begins with MTX_\n" +
				"$        End of name           _L$ matches only if name ends with _L\n" +
				".        Any single character  M.ge matches Mage, Moge, M_ge\n" +
				"*        Zero or more          Lo* matches L, Lo, Loo, Looo\n" +
				"+        One or more           Lo+ matches Lo, Loo but not L\n" +
				"?        Zero or one           Lo? matches L or Lo\n" +
				"[abc]    Any of these chars    [LR] matches L or R\n" +
				"[0-9]    Character range       [0-9] matches any digit\n" +
				"(a|b)    Either a or b         (Mage|Warrior) matches Mage or Warrior\n" +
				"\\d       Any digit             \\d+ matches one or more digits\n" +
				"\\w       Word character        \\w matches letters, digits, underscore\n" +
				"_L$      Practical example     Matches _L at the end of the name only"),
				EditorStyles.miniButton, GUILayout.Width(52));

			op.CaseSensitive = GUILayout.Toggle(op.CaseSensitive, new GUIContent("Case sensitive",
				"When on, the search must match the exact capitalisation.\n\n" +
				"Example:\n" +
				"Searching for 'mage' with Case Sensitive ON will not match 'Mage'.\n" +
				"With Case Sensitive OFF it matches regardless of capitalisation."),
				EditorStyles.miniButton, GUILayout.Width(100));

			op.MatchWholeWord = GUILayout.Toggle(op.MatchWholeWord, new GUIContent("Whole word",
				"When on, only matches the search term when it appears as a complete word — " +
				"not as part of a longer word.\n\n" +
				"Word boundaries in asset names include underscores, digits, and the start or end of the name.\n\n" +
				"Example:\n" +
				"Searching for 'Arm' with Whole Word ON:\n" +
				"  MTX_Arm_L     → matches  (Arm is bounded by underscores)\n" +
				"  MTX_ForeArm_L → no match (Arm is attached to Fore)\n\n" +
				"Searching for 'Arm' with Whole Word OFF:\n" +
				"  MTX_ForeArm_L → matches (finds Arm anywhere in the name)"),
				EditorStyles.miniButton, GUILayout.Width(82));
			EditorGUILayout.EndHorizontal();

			if (op.UseRegex && op.RegexError != null)
				EditorGUILayout.HelpBox($"Regex error: {op.RegexError}", MessageType.Error);
		}

		private void DrawOpPrefix(RenameOp op, int idx)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(new GUIContent("Prefix", "Text to prepend to the beginning of each name. Applied after any operations above it in the stack."), GUILayout.Width(80));
			GUI.SetNextControlName($"KSOp_{idx}_prefix");
			op.PrefixText = EditorGUILayout.TextField(op.PrefixText);
			EditorGUILayout.EndHorizontal();
		}

		private void DrawOpSuffix(RenameOp op, int idx)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(new GUIContent("Suffix", "Text to append to the end of each name. Applied after any operations above it in the stack."), GUILayout.Width(80));
			GUI.SetNextControlName($"KSOp_{idx}_suffix");
			op.SuffixText = EditorGUILayout.TextField(op.SuffixText);
			EditorGUILayout.EndHorizontal();
		}

		private void DrawOpNumber(RenameOp op, int idx)
		{
			EditorGUILayout.BeginHorizontal();
			// Single toolbar avoids the dual-toggle ambiguous state bug.
			// 0 = Append, 1 = Prepend
			EditorGUILayout.LabelField(new GUIContent("Position", "Whether the number is added at the end (Append) or the beginning (Prepend) of the name."), GUILayout.Width(58));
			int placement = GUI.Toolbar(
				EditorGUILayout.GetControlRect(GUILayout.Width(130)),
				op.NumberPrefix ? 1 : 0,
				new[] { "Append", "Prepend" },
				EditorStyles.miniButton);
			op.NumberPrefix = placement == 1;
			EditorGUILayout.LabelField(new GUIContent("Sep", "Separator inserted between the name and the number. Default is underscore. Can be left empty for no separator."), GUILayout.Width(28));
			GUI.SetNextControlName($"KSOp_{idx}_sep");
			op.NumberSep = EditorGUILayout.TextField(op.NumberSep, GUILayout.Width(36));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(new GUIContent("Start", "The number assigned to the first item in the list."), GUILayout.Width(38));
			op.NumberStart = EditorGUILayout.IntField(op.NumberStart, GUILayout.Width(44));
			EditorGUILayout.LabelField(new GUIContent("Step", "How much the number increases for each subsequent item. Default is 1."), GUILayout.Width(34));
			op.NumberStep = EditorGUILayout.IntField(op.NumberStep, GUILayout.Width(44));
			op.NumberStep = Mathf.Max(1, op.NumberStep);
			EditorGUILayout.LabelField(new GUIContent("Padding", "Minimum number of digits. Numbers are zero-padded to this width.\nPadding 2 → 01, 02 ... 10, 11\nPadding 3 → 001, 002 ... 010, 011"), GUILayout.Width(52));
			op.NumberPadding = EditorGUILayout.IntField(op.NumberPadding, GUILayout.Width(44));
			op.NumberPadding = Mathf.Clamp(op.NumberPadding, 1, 8);
			EditorGUILayout.EndHorizontal();
		}

		// ─────────────────────────────────────────────────────────────
		// Section — Options
		// ─────────────────────────────────────────────────────────────
		private void DrawOptionsSection()
		{
			EditorGUILayout.BeginVertical("box");
			EditorGUILayout.BeginHorizontal();

			using (new EditorGUI.DisabledScope(!_addressablesAvailable))
			{
				_syncAddressables = EditorGUILayout.ToggleLeft(
					new GUIContent("Sync Addressables address",
						_addressablesAvailable
							? "Updates the Addressables address string when renaming registered assets."
							: "Addressables package not detected in this project."),
					_syncAddressables && _addressablesAvailable);
			}

			GUILayout.FlexibleSpace();

			int matchCount = 0;
			foreach (var item in _items) if (item.WillChange) matchCount++;
			EditorGUILayout.LabelField(
				$"{_items.Count} item(s)  |  {matchCount} will change",
				EditorStyles.miniLabel);

			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
		}

		// ─────────────────────────────────────────────────────────────
		// Section — Type Filter Bar
		// ─────────────────────────────────────────────────────────────
		private void DrawTypeFilterBar()
		{
			if (_filterDirty)
			{
				RebuildFilterList();
				_filterDirty = false;
			}

			if (_availableFilters.Length == 0) return;

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(new GUIContent("Filter:", "Narrow the preview list to a specific asset type or GameObject. Does not affect which items get renamed — only what is visible here."), GUILayout.Width(40));

			bool allActive = _activeFilter == null;
			if (GUILayout.Toggle(allActive, new GUIContent("All", "Show all selected items regardless of type."),
					allActive ? _styleFilterBtnActive : _styleFilterBtn, GUILayout.Width(36))
				&& !allActive)
			{ _activeFilter = null; RebuildVisibleItems(); }

			foreach (var filter in _availableFilters)
			{
				bool active = _activeFilter == filter;
				float btnW = Mathf.Max(38f, filter.Length * 7f + 10f);
				if (GUILayout.Toggle(active, filter,
						active ? _styleFilterBtnActive : _styleFilterBtn, GUILayout.Width(btnW))
					&& !active)
				{ _activeFilter = filter; RebuildVisibleItems(); }
			}

			EditorGUILayout.EndHorizontal();
		}

		// ─────────────────────────────────────────────────────────────
		// Section — Preview Header
		// ─────────────────────────────────────────────────────────────
		private void DrawPreviewHeader()
		{
			if (_previewDirty)
			{
				RebuildPreviews();
				RebuildVisibleItems();
				_previewDirty = false;
			}

			Rect headerRect = EditorGUILayout.GetControlRect(false, HEADER_H);
			float halfW = (headerRect.width - COL_CHECKBOX_W - COL_ICON_W - COL_TYPE_W) * 0.5f;

			Rect rCheck = new Rect(headerRect.x, headerRect.y, COL_CHECKBOX_W, HEADER_H);
			Rect rIcon = new Rect(rCheck.xMax, headerRect.y, COL_ICON_W, HEADER_H);
			Rect rType = new Rect(rIcon.xMax, headerRect.y, COL_TYPE_W, HEADER_H);
			Rect rOrig = new Rect(rType.xMax, headerRect.y, halfW, HEADER_H);
			Rect rArrow = new Rect(rOrig.xMax, headerRect.y, 20f, HEADER_H);
			Rect rNew = new Rect(rArrow.xMax, headerRect.y, halfW - 20f, HEADER_H);

			EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, headerRect.width, HEADER_H),
				new Color(0.18f, 0.18f, 0.18f, 1f));

			bool allSel = _visibleItems.Count > 0 && _visibleItems.TrueForAll(i => i.Selected);
			bool newAll = EditorGUI.Toggle(new Rect(rCheck.x + 2, rCheck.y + 1, 16, 16), allSel);
			if (newAll != allSel)
				foreach (var item in _visibleItems) item.Selected = newAll;

			GUI.Label(new Rect(rType.x + 2, rType.y, rType.width, rType.height), "Type", _styleHeader);
			GUI.Label(new Rect(rOrig.x + 2, rOrig.y, rOrig.width, rOrig.height), "Original", _styleHeader);
			GUI.Label(new Rect(rNew.x + 2, rNew.y, rNew.width, rNew.height), "Renamed", _styleHeader);

			DrawSeparator();

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button(new GUIContent("Select All", "Check all visible items so they will be included in the rename."), EditorStyles.miniButton, GUILayout.Width(80)))
				foreach (var item in _visibleItems) item.Selected = true;
			if (GUILayout.Button(new GUIContent("Deselect All", "Uncheck all visible items so they will be excluded from the rename."), EditorStyles.miniButton, GUILayout.Width(80)))
				foreach (var item in _visibleItems) item.Selected = false;
			if (GUILayout.Button(new GUIContent("Invert", "Flip every checkbox — checked items become unchecked and vice versa."), EditorStyles.miniButton, GUILayout.Width(60)))
				foreach (var item in _visibleItems) item.Selected = !item.Selected;
			if (GUILayout.Button(new GUIContent("Changes Only", "Select only items whose name will actually change with the current operations."), EditorStyles.miniButton, GUILayout.Width(90)))
				foreach (var item in _visibleItems) item.Selected = item.WillChange;
			EditorGUILayout.EndHorizontal();
		}

		// ─────────────────────────────────────────────────────────────
		// Section — Preview List
		// ─────────────────────────────────────────────────────────────
		private void DrawPreviewList()
		{
			// Reserve enough space for the ops section (measured) + fixed chrome below it:
			// options row ~24, filter bar ~22, preview header ~56, footer ~34 = ~136 fixed
			// Use a safe minimum of 80px so the list never disappears entirely.
			float fixedChrome = 136f;
			float usedAbove = _opsAreaHeight > 0f ? _opsAreaHeight + fixedChrome : 480f;
			float listHeight = Mathf.Max(80f, position.height - usedAbove);
			_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(listHeight));

			for (int i = 0; i < _visibleItems.Count; i++)
			{
				var item = _visibleItems[i];
				Rect rowRect = EditorGUILayout.GetControlRect(false, PREVIEW_ROW_H);

				if (i % 2 == 0)
					EditorGUI.DrawRect(rowRect, new Color(0f, 0f, 0f, 0.07f));
				if (item.HasError)
					EditorGUI.DrawRect(rowRect, new Color(0.8f, 0.1f, 0.1f, 0.15f));
				else if (item.WillChange)
					EditorGUI.DrawRect(rowRect, new Color(0.1f, 0.5f, 0.1f, 0.10f));

				float halfW = (rowRect.width - COL_CHECKBOX_W - COL_ICON_W - COL_TYPE_W) * 0.5f;

				Rect rCheck = new Rect(rowRect.x, rowRect.y + 1, COL_CHECKBOX_W, PREVIEW_ROW_H - 2);
				Rect rIcon = new Rect(rCheck.xMax, rowRect.y + 1, COL_ICON_W, PREVIEW_ROW_H - 2);
				Rect rType = new Rect(rIcon.xMax, rowRect.y, COL_TYPE_W, PREVIEW_ROW_H);
				Rect rOrig = new Rect(rType.xMax, rowRect.y, halfW, PREVIEW_ROW_H);
				Rect rArrow = new Rect(rOrig.xMax, rowRect.y, 20f, PREVIEW_ROW_H);
				Rect rNew = new Rect(rArrow.xMax, rowRect.y, halfW - 20f, PREVIEW_ROW_H);

				item.Selected = EditorGUI.Toggle(rCheck, item.Selected);

				var icon = GetItemIcon(item);
				if (icon != null)
					GUI.DrawTexture(new Rect(rIcon.x, rIcon.y + 1, 16, 16), icon, ScaleMode.ScaleToFit);

				string typeLabel = item.Kind == ItemKind.GameObject ? "GO"
								 : item.Kind == ItemKind.Folder ? "Folder"
								 : string.IsNullOrEmpty(item.Extension) ? "Asset"
								 : item.Extension.TrimStart('.');
				GUI.Label(rType, typeLabel, EditorStyles.miniLabel);

				GUI.Label(rOrig, item.OriginalName, item.WillChange ? _styleOriginal : _styleUnchanged);

				if (item.WillChange)
					GUI.Label(rArrow, "→", EditorStyles.centeredGreyMiniLabel);

				if (item.HasError)
					GUI.Label(rNew, $"⚠ {item.ErrorMessage}", _styleError);
				else if (item.WillChange)
					DrawDiffName(rNew, item.PreviewName, item.HighlightRanges);
			}

			if (_visibleItems.Count == 0)
			{
				string msg = _items.Count == 0
					? "Select assets in the Project window or GameObjects in the Hierarchy to begin."
					: $"No items match the current filter ({_activeFilter}).";
				EditorGUILayout.HelpBox(msg, MessageType.Info);
			}

			EditorGUILayout.EndScrollView();
		}

		// ─────────────────────────────────────────────────────────────
		// Section — Footer / Apply
		// ─────────────────────────────────────────────────────────────
		private void DrawFooter()
		{
			DrawSeparator();

			int selTotal = 0, selChanges = 0;
			foreach (var item in _visibleItems)
			{
				if (!item.Selected) continue;
				selTotal++;
				if (item.WillChange && !item.HasError) selChanges++;
			}

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(
				$"{selTotal} selected  |  {selChanges} will be renamed  (Enter / Apply)",
				EditorStyles.miniLabel, GUILayout.MinWidth(260));
			GUILayout.FlexibleSpace();

			using (new EditorGUI.DisabledScope(selChanges == 0 || !HasAnyActiveOp()))
			{
				if (GUILayout.Button(new GUIContent("Apply", "Apply the rename to all checked items. Asset renames cannot be undone via Ctrl+Z. GameObject renames can be undone."), GUILayout.Width(90), GUILayout.Height(24)))
					TryApply();
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.Space(4);
		}

		// ─────────────────────────────────────────────────────────────
		// Data — Build item list from Selection
		// ─────────────────────────────────────────────────────────────
		private void RefreshItemsFromSelection()
		{
			_items.Clear();

			var selected = Selection.objects;
			if (selected == null || selected.Length == 0) return;

			foreach (var obj in selected)
			{
				if (obj == null) continue;

				string assetPath = AssetDatabase.GetAssetPath(obj);
				bool isAsset = !string.IsNullOrEmpty(assetPath);
				bool isFolder = isAsset && AssetDatabase.IsValidFolder(assetPath);

				_items.Add(new RenameItem
				{
					Target = obj,
					OriginalName = obj.name,
					PreviewName = obj.name,
					AssetPath = assetPath,
					Kind = !isAsset ? ItemKind.GameObject
								 : isFolder ? ItemKind.Folder
											: ItemKind.Asset,
					Extension = (!isAsset || isFolder)
								 ? string.Empty
								 : Path.GetExtension(assetPath).ToLowerInvariant(),
				});
			}

			_previewDirty = true;
			_filterDirty = true;
		}

		// ─────────────────────────────────────────────────────────────
		// Data — Filter list
		// ─────────────────────────────────────────────────────────────
		private void RebuildFilterList()
		{
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var filters = new List<string>();

			foreach (var item in _items)
			{
				string key = FilterKeyFor(item);
				if (seen.Add(key)) filters.Add(key);
			}

			filters.Sort((a, b) =>
			{
				bool aSpec = a == "GO" || a == "Folder" || a == "Asset";
				bool bSpec = b == "GO" || b == "Folder" || b == "Asset";
				if (aSpec != bSpec) return aSpec ? 1 : -1;
				return string.Compare(a, b, StringComparison.Ordinal);
			});

			_availableFilters = filters.ToArray();

			if (_activeFilter != null && Array.IndexOf(_availableFilters, _activeFilter) < 0)
				_activeFilter = null;

			RebuildVisibleItems();
		}

		private void RebuildVisibleItems()
		{
			_visibleItems.Clear();
			foreach (var item in _items)
				if (_activeFilter == null || FilterKeyFor(item) == _activeFilter)
					_visibleItems.Add(item);
		}

		private static string FilterKeyFor(RenameItem item)
		{
			if (item.Kind == ItemKind.GameObject) return "GO";
			if (item.Kind == ItemKind.Folder) return "Folder";
			return string.IsNullOrEmpty(item.Extension) ? "Asset" : item.Extension;
		}

		// ─────────────────────────────────────────────────────────────
		// Data — Preview computation
		// Applies all enabled ops in order. Number op uses the item's
		// position in _items (not _visibleItems) so numbering is stable
		// regardless of the active filter.
		// ─────────────────────────────────────────────────────────────
		private void RebuildPreviews()
		{
			// Pre-compile regexes and reset errors
			foreach (var op in _ops)
			{
				op.RegexError = null;
				op.CompiledRegex = null;

				if (op.OpType != OpType.SearchReplace || !op.Enabled || !op.UseRegex) continue;
				if (string.IsNullOrEmpty(op.SearchText)) continue;

				try
				{
					var opts = op.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
					op.CompiledRegex = new Regex(op.SearchText, opts);
				}
				catch (ArgumentException ex) { op.RegexError = ex.Message; }
			}

			// Build per-op number counters (one counter per Number op, indexed by op position)
			var numberCounters = new Dictionary<int, int>();
			for (int i = 0; i < _ops.Count; i++)
				if (_ops[i].OpType == OpType.Number && _ops[i].Enabled)
					numberCounters[i] = _ops[i].NumberStart;

			foreach (var item in _items)
			{
				item.HasError = false;
				item.ErrorMessage = null;
				item.HighlightRanges.Clear();

				string name = item.OriginalName;

				for (int i = 0; i < _ops.Count; i++)
				{
					var op = _ops[i];
					if (!op.Enabled) continue;

					switch (op.OpType)
					{
						case OpType.SearchReplace:
							name = ApplySearchReplaceTracked(name, op, item.HighlightRanges);
							break;

						case OpType.Prefix:
							if (!string.IsNullOrEmpty(op.PrefixText))
							{
								// Shift all existing ranges right by prefix length
								ShiftRanges(item.HighlightRanges, op.PrefixText.Length);
								item.HighlightRanges.Add((0, op.PrefixText.Length));
								name = op.PrefixText + name;
							}
							break;

						case OpType.Suffix:
							if (!string.IsNullOrEmpty(op.SuffixText))
							{
								item.HighlightRanges.Add((name.Length, op.SuffixText.Length));
								name = name + op.SuffixText;
							}
							break;

						case OpType.Number:
							int counter = numberCounters[i];
							string numStr = counter.ToString().PadLeft(op.NumberPadding, '0');
							string numFull = op.NumberPrefix
								? numStr + op.NumberSep
								: op.NumberSep + numStr;
							if (op.NumberPrefix)
							{
								ShiftRanges(item.HighlightRanges, numFull.Length);
								item.HighlightRanges.Add((0, numFull.Length));
								name = numFull + name;
							}
							else
							{
								item.HighlightRanges.Add((name.Length, numFull.Length));
								name = name + numFull;
							}
							numberCounters[i] = counter + op.NumberStep;
							break;
					}
				}

				item.PreviewName = name;
			}

			// Batch collision check
			var seen = new Dictionary<string, RenameItem>(StringComparer.Ordinal);
			foreach (var item in _items)
			{
				if (!item.WillChange) continue;
				if (seen.TryGetValue(item.PreviewName, out var conflict))
				{
					item.HasError = true;
					item.ErrorMessage = "Duplicate in batch";
					conflict.HasError = true;
					conflict.ErrorMessage = "Duplicate in batch";
				}
				else seen[item.PreviewName] = item;
			}
		}

		/// <summary>
		/// Applies search/replace to name, records the resulting highlight ranges
		/// for every replacement made (i.e. where the replace text landed in the output).
		/// </summary>
		private string ApplySearchReplaceTracked(string name, RenameOp op, List<(int, int)> ranges)
		{
			if (string.IsNullOrEmpty(op.SearchText)) return name;

			string replace = op.ReplaceText ?? string.Empty;
			var sb = new System.Text.StringBuilder(name.Length);
			int idx = 0;

			if (op.UseRegex)
			{
				if (op.CompiledRegex == null) return name;

				int outPos = 0;
				// Use regex.Matches to find all match spans, build output manually
				var matches = op.CompiledRegex.Matches(name);
				foreach (System.Text.RegularExpressions.Match m in matches)
				{
					sb.Append(name, idx, m.Index - idx);
					outPos = sb.Length;
					sb.Append(replace);
					if (replace.Length > 0)
						ranges.Add((outPos, replace.Length));
					idx = m.Index + m.Length;
				}
				sb.Append(name, idx, name.Length - idx);
				return sb.ToString();
			}

			string search = op.SearchText;

			if (op.MatchWholeWord)
			{
				var opts = op.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
				string pattern = @"\b" + Regex.Escape(search) + @"\b";
				var matches = Regex.Matches(name, pattern, opts);
				int outPos = 0;
				foreach (System.Text.RegularExpressions.Match m in matches)
				{
					sb.Append(name, idx, m.Index - idx);
					outPos = sb.Length;
					sb.Append(replace);
					if (replace.Length > 0)
						ranges.Add((outPos, replace.Length));
					idx = m.Index + m.Length;
				}
				sb.Append(name, idx, name.Length - idx);
				return sb.ToString();
			}

			// Plain string replace — find all occurrences manually
			StringComparison cmp = op.CaseSensitive
				? StringComparison.Ordinal
				: StringComparison.OrdinalIgnoreCase;

			while (true)
			{
				int found = name.IndexOf(search, idx, cmp);
				if (found < 0)
				{
					sb.Append(name, idx, name.Length - idx);
					break;
				}
				sb.Append(name, idx, found - idx);
				int outPos = sb.Length;
				sb.Append(replace);
				if (replace.Length > 0)
					ranges.Add((outPos, replace.Length));
				idx = found + search.Length;
			}
			return sb.ToString();
		}

		/// <summary>Shifts all existing range start positions right by delta characters.</summary>
		private static void ShiftRanges(List<(int start, int length)> ranges, int delta)
		{
			for (int i = 0; i < ranges.Count; i++)
				ranges[i] = (ranges[i].start + delta, ranges[i].length);
		}

		private static string ReplaceIgnoreCase(string input, string search, string replacement)
		{
			var sb = new System.Text.StringBuilder(input.Length);
			int idx = 0;
			while (true)
			{
				int found = input.IndexOf(search, idx, StringComparison.OrdinalIgnoreCase);
				if (found < 0) { sb.Append(input, idx, input.Length - idx); break; }
				sb.Append(input, idx, found - idx);
				sb.Append(replacement);
				idx = found + search.Length;
			}
			return sb.ToString();
		}

		// ─────────────────────────────────────────────────────────────
		// Apply
		// ─────────────────────────────────────────────────────────────
		private void TryApply()
		{
			int assetCount = 0, goCount = 0;
			foreach (var item in _visibleItems)
			{
				if (!item.Selected || !item.WillChange || item.HasError) continue;
				if (item.Kind == ItemKind.GameObject) goCount++;
				else assetCount++;
			}

			// Only warn about non-undoable asset renames — GO renames are fully undoable
			if (assetCount > 0)
			{
				string assetLine = assetCount == 1 ? "1 asset" : $"{assetCount} assets";
				string goLine = goCount == 0 ? string.Empty
								 : goCount == 1 ? " and 1 GameObject"
								 : $" and {goCount} GameObjects";
				bool ok = EditorUtility.DisplayDialog(
					"KS Smart Rename — Confirm",
					$"Rename {assetLine}{goLine}.\n\n" +
					"Asset renames cannot be undone via Ctrl+Z.\n" +
					"GUID references will be preserved.\n\nContinue?",
					"Rename", "Cancel");
				if (!ok) return;
			}
			// Pure GO renames are fully undoable — proceed without confirmation

			int renamed = 0, failed = 0;

			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (var item in _visibleItems)
				{
					if (!item.Selected || !item.WillChange || item.HasError) continue;
					if (item.Kind == ItemKind.GameObject)
					{ RenameGameObject(item); renamed++; }
					else
					{ if (RenameAsset(item)) renamed++; else failed++; }
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			Debug.Log($"[KS SmartRename] Renamed {renamed}. Failed: {failed}.");

			if (failed > 0)
				EditorUtility.DisplayDialog("KS Smart Rename",
					$"Renamed {renamed} item(s).\n{failed} failed — see Console for details.", "OK");

			RefreshItemsFromSelection();
		}

		private bool RenameAsset(RenameItem item)
		{
			if (string.IsNullOrEmpty(item.AssetPath))
			{
				Debug.LogWarning($"[KS SmartRename] No asset path for '{item.OriginalName}'.");
				return false;
			}

			string error = AssetDatabase.RenameAsset(item.AssetPath, item.PreviewName);
			if (!string.IsNullOrEmpty(error))
			{
				Debug.LogError($"[KS SmartRename] '{item.OriginalName}' → '{item.PreviewName}': {error}");
				return false;
			}

			if (_syncAddressables && _addressablesAvailable)
				TrySyncAddressablesAddress(item.AssetPath, item.OriginalName, item.PreviewName);

			Debug.Log($"[KS SmartRename] Asset: '{item.OriginalName}' → '{item.PreviewName}'");
			return true;
		}

		private void RenameGameObject(RenameItem item)
		{
			Undo.RecordObject(item.Target, $"KS SmartRename: {item.OriginalName} → {item.PreviewName}");
			item.Target.name = item.PreviewName;
			EditorUtility.SetDirty(item.Target);
			Debug.Log($"[KS SmartRename] GO: '{item.OriginalName}' → '{item.PreviewName}'");
		}

		// ─────────────────────────────────────────────────────────────
		// Addressables — Reflection Detection & Sync
		// ─────────────────────────────────────────────────────────────
		private static void EnsureAddressablesReflection()
		{
			if (_addressablesChecked) return;
			_addressablesChecked = true;

			try
			{
				foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
				{
					if (!asm.FullName.Contains("Addressables")) continue;

					_typeDefaultObject = asm.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
					_typeSettings = asm.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
					var typeGroup = asm.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetGroup");
					var typeEntry = asm.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetEntry");
					var typeModEvent = asm.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings+ModificationEvent");

					if (_typeDefaultObject == null || _typeSettings == null ||
						typeGroup == null || typeEntry == null) continue;

					_propDefaultSettings = _typeDefaultObject.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
					_propGroups = _typeSettings.GetProperty("groups", BindingFlags.Public | BindingFlags.Instance);
					_methodGetEntry = typeGroup.GetMethod("GetAssetEntry", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
					_propAddress = typeEntry.GetProperty("address", BindingFlags.Public | BindingFlags.Instance);
					_methodSetAddress = typeEntry.GetMethod("SetAddress", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);

					if (typeModEvent != null)
						_methodSetDirty = _typeSettings.GetMethod("SetDirty", BindingFlags.Public | BindingFlags.Instance, null,
							new[] { typeModEvent, typeof(object), typeof(bool) }, null);

					_addressablesAvailable =
						_propDefaultSettings != null && _propGroups != null &&
						_methodGetEntry != null && _propAddress != null &&
						_methodSetAddress != null;

					if (_addressablesAvailable)
						Debug.Log("[KS SmartRename] Addressables detected — address sync enabled.");
					break;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[KS SmartRename] Addressables reflection failed: {ex.Message}");
			}
		}

		private static void TrySyncAddressablesAddress(string oldPath, string oldName, string newName)
		{
			if (!_addressablesAvailable) return;
			try
			{
				var settings = _propDefaultSettings.GetValue(null);
				if (settings == null) return;

				string guid = AssetDatabase.AssetPathToGUID(oldPath);
				if (string.IsNullOrEmpty(guid)) return;

				var groups = _propGroups.GetValue(settings) as System.Collections.IEnumerable;
				if (groups == null) return;

				foreach (var group in groups)
				{
					if (group == null) continue;
					var entry = _methodGetEntry.Invoke(group, new object[] { guid });
					if (entry == null) continue;

					string current = _propAddress.GetValue(entry) as string ?? string.Empty;
					if (!current.Contains(oldName)) break;

					string updated = current.Replace(oldName, newName);
					_methodSetAddress.Invoke(entry, new object[] { updated });
					Debug.Log($"[KS SmartRename] Addressables: '{current}' → '{updated}'");

					if (_methodSetDirty != null)
					{
						var ev = Enum.ToObject(_methodSetDirty.GetParameters()[0].ParameterType, 3);
						_methodSetDirty.Invoke(settings, new object[] { ev, null, true });
					}
					break;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[KS SmartRename] Addressables sync failed: {ex.Message}");
			}
		}

		// ─────────────────────────────────────────────────────────────
		// Drawing Helpers
		// ─────────────────────────────────────────────────────────────
		private Texture GetItemIcon(RenameItem item)
		{
			if (item.Kind == ItemKind.GameObject) return EditorGUIUtility.IconContent("GameObject Icon").image;
			if (item.Kind == ItemKind.Folder) return EditorGUIUtility.IconContent("Folder Icon").image;
			return AssetDatabase.GetCachedIcon(item.AssetPath);
		}

		private void DrawSeparator()
		{
			var r = EditorGUILayout.GetControlRect(false, 1f);
			EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin
				? new Color(0.15f, 0.15f, 0.15f, 1f)
				: new Color(0.60f, 0.60f, 0.60f, 1f));
		}

		/// <summary>
		/// Draws the renamed name using precomputed highlight ranges from RebuildPreviews.
		/// Ranges mark exactly which characters each op contributed — no post-hoc inference.
		/// Characters inside a range are drawn in highlight style with a blue background.
		/// Characters outside ranges are drawn in normal changed (green) style.
		/// </summary>
		private void DrawDiffName(Rect rect, string renamed, List<(int start, int length)> ranges)
		{
			if (string.IsNullOrEmpty(renamed)) return;

			// Build a per-character bool mask from the ranges
			bool[] highlighted = new bool[renamed.Length];
			foreach (var (start, length) in ranges)
			{
				int end = Mathf.Min(start + length, renamed.Length);
				for (int k = start; k < end; k++)
					highlighted[k] = true;
			}

			float x = rect.x + 2f;
			float h = rect.height;
			int i = 0;

			while (i < renamed.Length)
			{
				bool runHL = highlighted[i];
				int j = i;
				while (j < renamed.Length && highlighted[j] == runHL) j++;

				string segment = renamed.Substring(i, j - i);
				GUIStyle style = runHL ? _styleHighlight : _styleChanged;
				float w = style.CalcSize(new GUIContent(segment)).x;

				if (x + w > rect.xMax) w = rect.xMax - x;
				if (w <= 0f) break;

				if (runHL)
				{
					EditorGUI.DrawRect(
						new Rect(x - 1f, rect.y + 1f, w + 2f, h - 2f),
						new Color(0.15f, 0.55f, 0.9f, 0.30f));
				}

				GUI.Label(new Rect(x, rect.y, w, h), segment, style);
				x += w;
				i = j;
			}
		}

		// ─────────────────────────────────────────────────────────────
		// Style Init
		// ─────────────────────────────────────────────────────────────
		private void EnsureStyles()
		{
			if (_stylesReady) return;

			_styleOriginal = new GUIStyle(EditorStyles.miniLabel)
			{
				normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.2f, 0.2f, 0.2f) }
			};
			_styleChanged = new GUIStyle(EditorStyles.miniLabel)
			{
				normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.45f, 0.9f, 0.45f) : new Color(0.0f, 0.4f, 0.0f) },
				fontStyle = FontStyle.Bold
			};
			_styleUnchanged = new GUIStyle(EditorStyles.miniLabel)
			{
				normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.55f, 0.55f, 0.55f) }
			};
			_styleError = new GUIStyle(EditorStyles.miniLabel)
			{
				normal = { textColor = new Color(0.9f, 0.3f, 0.3f) },
				fontStyle = FontStyle.Bold
			};
			_styleHeader = new GUIStyle(EditorStyles.miniLabel)
			{
				normal = { textColor = Color.white },
				fontStyle = FontStyle.Bold
			};
			_styleFilterBtn = new GUIStyle(EditorStyles.miniButton)
			{
				padding = new RectOffset(4, 4, 2, 2),
				margin = new RectOffset(2, 2, 0, 0),
				fontSize = 10
			};
			_styleFilterBtnActive = new GUIStyle(_styleFilterBtn)
			{
				normal = { textColor  = EditorGUIUtility.isProSkin ? Color.white : Color.black,
							  background = _styleFilterBtn.active.background },
				fontStyle = FontStyle.Bold
			};
			_styleOpBox = new GUIStyle("box")
			{
				padding = new RectOffset(6, 6, 4, 4),
				margin = new RectOffset(0, 0, 0, 0)
			};
			// Highlighted (added/changed) characters in the diff — brighter, blue-tinted
			_styleHighlight = new GUIStyle(EditorStyles.miniLabel)
			{
				normal = { textColor = EditorGUIUtility.isProSkin
					? new Color(0.45f, 0.85f, 1.00f)   // bright cyan-blue on dark skin
                    : new Color(0.00f, 0.30f, 0.75f) }, // dark blue on light skin
				fontStyle = FontStyle.Bold
			};

			_stylesReady = true;
		}
	}
}
#endif
