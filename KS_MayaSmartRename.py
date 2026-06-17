# -*- coding: utf-8 -*-
"""
===============================================================================
KS_MayaSmartRename — Maya bulk rename helper
Author: Kevin Silvagnoli / KS Tool Family
Version: 0.2.3
Tested target: Maya 2022+ / Maya 2026.2

PURPOSE
- Fast, safe bulk renaming for Maya scene objects.
- Supports:
    1. Sequential Rename
    2. Search / Replace
    3. Prefix / Suffix Only

USAGE
    import importlib
    import KS_MayaSmartRename
    importlib.reload(KS_MayaSmartRename)
    KS_MayaSmartRename.show()
===============================================================================
"""
from __future__ import annotations

import os
import re
import json

try:
    import maya.cmds as cmds
except Exception:
    class _Dummy(object):
        def __getattr__(self, k):
            return lambda *a, **kw: None
    cmds = _Dummy()

try:
    from PySide2 import QtWidgets, QtCore, QtGui
except ImportError:
    from PySide6 import QtWidgets, QtCore, QtGui


C_BG          = "#2b2b2b"
C_BG_ALT      = "#333333"
C_BG_INPUT    = "#3a3a3a"
C_BG_DARK     = "#252525"
C_BORDER      = "#1a1a1a"
C_BORDER_MID  = "#555555"
C_TEXT        = "#cccccc"
C_TEXT_DIM    = "#888888"
C_TEXT_MUTED  = "#555555"
C_ACCENT      = "#5285a6"
C_ACCENT_GRN  = "#6a9a6a"
C_COUNT_LBL   = "#6abf69"
C_DANGER      = "#e05050"

WINDOW_OBJECT = "KSMayaSmartRenameUI"
WINDOW_TITLE = "KS Maya Smart Rename"

MODE_SEQUENTIAL = "Sequential Rename"
MODE_SEARCH_REPLACE = "Search / Replace"
MODE_PREFIX_SUFFIX = "Prefix / Suffix Only"

_PREFS_FILENAME = "KS_MayaSmartRename_prefs.json"


def _ks_stylesheet():
    return f"""
#{WINDOW_OBJECT}, #{WINDOW_OBJECT} QWidget {{
    background-color: {C_BG};
    color: {C_TEXT};
    font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif;
    font-size: 11px;
}}
#{WINDOW_OBJECT} QLabel {{
    background: transparent;
    color: {C_TEXT};
}}
#{WINDOW_OBJECT} QLineEdit {{
    background-color: {C_BG_INPUT};
    color: {C_TEXT};
    border: 1px solid {C_BORDER_MID};
    border-radius: 3px;
    padding: 2px 5px;
    selection-background-color: {C_ACCENT};
}}
#{WINDOW_OBJECT} QLineEdit:focus {{
    border-color: {C_ACCENT};
}}
#{WINDOW_OBJECT} QPushButton {{
    background-color: {C_BG_INPUT};
    color: {C_TEXT};
    border: 1px solid {C_BORDER_MID};
    border-radius: 3px;
    padding: 3px 10px;
}}
#{WINDOW_OBJECT} QPushButton:hover {{
    background-color: #484848;
    border-color: {C_ACCENT};
}}
#{WINDOW_OBJECT} QPushButton:pressed {{
    background-color: {C_ACCENT};
    border-color: {C_ACCENT};
}}
#{WINDOW_OBJECT} QPushButton:disabled {{
    color: {C_TEXT_MUTED};
    background-color: {C_BG};
    border-color: #3a3a3a;
}}
#{WINDOW_OBJECT} QCheckBox {{
    color: {C_TEXT};
    spacing: 5px;
    background: transparent;
}}
#{WINDOW_OBJECT} QCheckBox::indicator {{
    width: 13px;
    height: 13px;
    border: 1px solid {C_BORDER_MID};
    border-radius: 2px;
    background-color: {C_BG_INPUT};
    image: none;
}}
#{WINDOW_OBJECT} QCheckBox::indicator:checked {{
    background-color: {C_ACCENT_GRN};
    border-color: {C_ACCENT_GRN};
    image: none;
}}
#{WINDOW_OBJECT} QCheckBox::indicator:unchecked {{
    background-color: {C_BG_INPUT};
    border-color: {C_BORDER_MID};
    image: none;
}}
#{WINDOW_OBJECT} QSpinBox {{
    background-color: {C_BG_INPUT};
    color: {C_TEXT};
    border: 1px solid {C_BORDER_MID};
    border-radius: 3px;
    padding: 1px 4px;
}}
#{WINDOW_OBJECT} QSpinBox:focus {{
    border-color: {C_ACCENT};
}}
#{WINDOW_OBJECT} QComboBox {{
    background-color: {C_BG_INPUT};
    color: {C_TEXT};
    border: 1px solid {C_BORDER_MID};
    border-radius: 3px;
    padding: 2px 6px;
}}
#{WINDOW_OBJECT} QComboBox:focus {{
    border-color: {C_ACCENT};
}}
#{WINDOW_OBJECT} QComboBox::drop-down {{
    border: none;
    width: 18px;
}}
#{WINDOW_OBJECT} QComboBox QAbstractItemView {{
    background-color: {C_BG_ALT};
    color: {C_TEXT};
    border: 1px solid {C_BORDER_MID};
    selection-background-color: {C_ACCENT};
}}
#{WINDOW_OBJECT} QTableWidget {{
    background-color: {C_BG_DARK};
    alternate-background-color: {C_BG_ALT};
    color: {C_TEXT};
    border: 1px solid #444444;
    border-radius: 3px;
    gridline-color: #3a3a3a;
    outline: none;
}}
#{WINDOW_OBJECT} QTableWidget::item:selected {{
    background-color: {C_ACCENT};
    color: #ffffff;
}}
#{WINDOW_OBJECT} QHeaderView::section {{
    background-color: {C_BG_ALT};
    color: {C_TEXT_DIM};
    border: none;
    border-right: 1px solid {C_BORDER};
    border-bottom: 1px solid #444444;
    padding: 3px 6px;
    font-weight: bold;
    font-size: 10px;
}}
#{WINDOW_OBJECT} QScrollBar:vertical {{
    background: {C_BG};
    width: 8px;
    margin: 0;
    border: none;
}}
#{WINDOW_OBJECT} QScrollBar::handle:vertical {{
    background: #555555;
    border-radius: 4px;
    min-height: 20px;
}}
#{WINDOW_OBJECT} QScrollBar::add-line:vertical,
#{WINDOW_OBJECT} QScrollBar::sub-line:vertical {{
    height: 0;
}}
#{WINDOW_OBJECT} QSplitter::handle {{
    background-color: {C_BORDER};
    height: 3px;
}}
#{WINDOW_OBJECT} QSplitter::handle:hover {{
    background-color: {C_ACCENT};
}}
QToolTip {{
    background-color: #3a3a3a;
    color: {C_TEXT};
    border: 1px solid {C_BORDER_MID};
    padding: 3px 6px;
    border-radius: 3px;
}}
"""


def _prefs_path():
    try:
        prefs_dir = cmds.internalVar(userPrefDir=True)
    except Exception:
        prefs_dir = os.path.expanduser("~")
    return os.path.join(prefs_dir, _PREFS_FILENAME)


def _load_json(path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def _save_json(path, data):
    try:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
        return True
    except Exception:
        return False


def _short_name(path):
    return (path or "").split("|")[-1]


def _node_type(path):
    try:
        if cmds.objExists(path):
            return cmds.nodeType(path)
    except Exception:
        pass
    return "unknown"


def _safe_name_piece(text):
    text = text or ""
    text = re.sub(r"\s+", "_", text.strip())
    text = re.sub(r"[^A-Za-z0-9_]", "_", text)
    text = re.sub(r"_+", "_", text)
    return text.strip("_")


def _is_valid_maya_short_name(name):
    if not name:
        return False, "Empty name"

    if re.match(r"^[0-9]", name):
        return False, "Cannot start with number"

    if not re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", name):
        return False, "Invalid Maya characters"

    return True, ""


def _unique_preserve_order(items):
    seen = set()
    out = []
    for x in items or []:
        if x in seen:
            continue
        seen.add(x)
        out.append(x)
    return out


def _replace_plain(name, search, replace, case_sensitive):
    if not search:
        return name

    if case_sensitive:
        return name.replace(search, replace)

    pattern = re.compile(re.escape(search), re.IGNORECASE)
    return pattern.sub(replace, name)


def _replace_whole_word(name, search, replace, case_sensitive):
    if not search:
        return name

    flags = 0 if case_sensitive else re.IGNORECASE
    pattern = re.compile(
        r"(?<![A-Za-z0-9])" + re.escape(search) + r"(?![A-Za-z0-9])",
        flags
    )
    return pattern.sub(replace, name)


class _SectionHeader(QtWidgets.QWidget):
    toggled = QtCore.Signal(bool)

    def __init__(self, title, expanded=True, parent=None):
        super(_SectionHeader, self).__init__(parent)
        self._expanded = expanded

        layout = QtWidgets.QHBoxLayout(self)
        layout.setContentsMargins(6, 5, 6, 5)
        layout.setSpacing(6)

        self._arrow = QtWidgets.QLabel()
        self._arrow.setFixedWidth(16)
        self._arrow.setStyleSheet(
            f"color: {C_TEXT}; font-size: 13px; background: transparent;"
        )
        self._update_arrow()

        label = QtWidgets.QLabel(title.upper())
        label.setAlignment(QtCore.Qt.AlignCenter)
        label.setStyleSheet(
            f"font-weight: bold; font-size: 10px; letter-spacing: 1px; "
            f"color: {C_TEXT}; background: transparent;"
        )

        line_l = QtWidgets.QFrame()
        line_l.setFrameShape(QtWidgets.QFrame.HLine)
        line_l.setStyleSheet("color: #444444;")

        line_r = QtWidgets.QFrame()
        line_r.setFrameShape(QtWidgets.QFrame.HLine)
        line_r.setStyleSheet("color: #444444;")

        layout.addWidget(self._arrow)
        layout.addWidget(line_l, 1)
        layout.addWidget(label)
        layout.addWidget(line_r, 1)

        self.setCursor(QtCore.Qt.PointingHandCursor)
        self.setStyleSheet(
            f"background-color: {C_BG_DARK}; border-bottom: 1px solid #3a3a3a;"
        )

    def _update_arrow(self):
        self._arrow.setText("▾" if self._expanded else "▸")

    def mousePressEvent(self, event):
        if event.button() == QtCore.Qt.LeftButton:
            self._expanded = not self._expanded
            self._update_arrow()
            self.toggled.emit(self._expanded)


class CollapsibleSection(QtWidgets.QWidget):
    def __init__(self, title="", expanded=True, parent=None):
        super(CollapsibleSection, self).__init__(parent)

        self.header = _SectionHeader(title, expanded=expanded)
        self.header.toggled.connect(self._apply_expanded)

        self.contentWidget = QtWidgets.QWidget()
        self.contentLayout = QtWidgets.QVBoxLayout(self.contentWidget)
        self.contentLayout.setContentsMargins(4, 4, 4, 4)
        self.contentLayout.setSpacing(4)

        main = QtWidgets.QVBoxLayout(self)
        main.setContentsMargins(0, 0, 0, 0)
        main.setSpacing(0)
        main.addWidget(self.header)
        main.addWidget(self.contentWidget)

        self._apply_expanded(expanded)

    def _apply_expanded(self, expanded):
        self.contentWidget.setVisible(expanded)

    def addWidget(self, widget, stretch=0):
        self.contentLayout.addWidget(widget, stretch)

    def addLayout(self, layout, stretch=0):
        self.contentLayout.addLayout(layout, stretch)


class RenameItem(object):
    def __init__(self, path):
        self.path = path
        self.original = _short_name(path)
        self.preview = self.original
        self.enabled = True
        self.error = ""

    @property
    def will_change(self):
        return self.original != self.preview


class KSMayaSmartRenameUI(QtWidgets.QDialog):
    def __init__(self, parent=None):
        super(KSMayaSmartRenameUI, self).__init__(parent)
        self.setWindowFlags(self.windowFlags() | QtCore.Qt.Tool)
        self.setWindowTitle(WINDOW_TITLE)
        self.setObjectName(WINDOW_OBJECT)
        self.resize(820, 720)
        self.setMinimumSize(720, 540)
        self.setStyleSheet(_ks_stylesheet())

        self._items = []
        self._building_table = False

        self._build_widgets()
        self._build_layout()
        self._connect_signals()
        self._load_settings_from_prefs(silent=True)
        self._on_mode_changed()
        self._refresh_preview()

    def _build_widgets(self):
        self.statusLbl = QtWidgets.QLabel("Items: 0 | Will rename: 0")
        self.statusLbl.setToolTip(
            "Shows how many objects are loaded into the tool and how many will be renamed with the current settings."
        )
        self.statusLbl.setStyleSheet(f"color: {C_COUNT_LBL}; font-weight: bold;")

        self.loadSelBtn = QtWidgets.QPushButton("Load Current Selection")
        self.loadSelBtn.setToolTip(
            "Clears the current tool list and loads the current Maya selection.\n\n"
            "Use this when starting a new rename pass. The order Maya returns the selection becomes the rename order."
        )
        self.loadSelBtn.setStyleSheet(
            f"QPushButton {{ background-color: #3a5a3a; border-color: {C_ACCENT_GRN}; font-weight: bold; }}"
            f"QPushButton:hover {{ background-color: #4a6a4a; }}"
        )

        self.addSelBtn = QtWidgets.QPushButton("Add Selected")
        self.addSelBtn.setToolTip(
            "Adds the current Maya selection to the existing list without clearing it.\n\n"
            "Objects already in the list are skipped."
        )

        self.removeBtn = QtWidgets.QPushButton("Remove")
        self.removeBtn.setToolTip(
            "Removes the selected rows from the tool list.\n\n"
            "This does not delete anything from the Maya scene."
        )

        self.clearBtn = QtWidgets.QPushButton("Clear")
        self.clearBtn.setToolTip(
            "Clears all objects from the tool list.\n\n"
            "This does not affect the Maya scene."
        )

        self.moveUpBtn = QtWidgets.QPushButton("Move Up")
        self.moveUpBtn.setToolTip(
            "Moves the selected row up in the rename order.\n\n"
            "Useful for joint chains where numbering depends on the order."
        )

        self.moveDownBtn = QtWidgets.QPushButton("Move Down")
        self.moveDownBtn.setToolTip(
            "Moves the selected row down in the rename order.\n\n"
            "Useful for joint chains where numbering depends on the order."
        )

        self.reverseBtn = QtWidgets.QPushButton("Reverse")
        self.reverseBtn.setToolTip(
            "Reverses the full list order.\n\n"
            "Useful when a joint chain was selected from tip to root instead of root to tip."
        )

        self.selectInMayaBtn = QtWidgets.QPushButton("Select Rows in Maya")
        self.selectInMayaBtn.setToolTip(
            "Selects the highlighted table rows in Maya.\n\n"
            "If no rows are highlighted, all enabled rows are selected instead."
        )

        self.table = QtWidgets.QTableWidget(0, 6)
        self.table.setToolTip(
            "Rename preview table.\n\n"
            "Use: whether this row will be included.\n"
            "Current Name: the object's current Maya name.\n"
            "Preview Name: the name that will be applied.\n"
            "Status: warnings such as duplicate names, invalid characters, or existing scene names."
        )
        self.table.setHorizontalHeaderLabels(["Use", "Type", "Current Name", "→", "Preview Name", "Status"])
        self.table.setAlternatingRowColors(True)
        self.table.setSelectionBehavior(QtWidgets.QAbstractItemView.SelectRows)
        self.table.setSelectionMode(QtWidgets.QAbstractItemView.ExtendedSelection)
        self.table.verticalHeader().setVisible(False)
        self.table.horizontalHeader().setStretchLastSection(True)
        self.table.setColumnWidth(0, 42)
        self.table.setColumnWidth(1, 80)
        self.table.setColumnWidth(2, 225)
        self.table.setColumnWidth(3, 28)
        self.table.setColumnWidth(4, 250)

        self.modeCombo = QtWidgets.QComboBox()
        self.modeCombo.setToolTip(
            "Choose how names are generated.\n\n"
            "Sequential Rename:\n"
            "Creates a new numbered name for each item, such as Spine_01_JNT.\n\n"
            "Search / Replace:\n"
            "Finds text in each current name and replaces it.\n\n"
            "Prefix / Suffix Only:\n"
            "Keeps the current name and only adds text before and/or after it."
        )
        self.modeCombo.addItems([MODE_SEQUENTIAL, MODE_SEARCH_REPLACE, MODE_PREFIX_SUFFIX])

        self.prefixEdit = QtWidgets.QLineEdit("")
        self.prefixEdit.setPlaceholderText("Prefix, e.g. L_, R_, FK_")
        self.prefixEdit.setToolTip(
            "Text added before the generated name.\n\n"
            "Sequential example:\n"
            "Prefix L_ + Arm_01_JNT → L_Arm_01_JNT\n\n"
            "Prefix / Suffix Only example:\n"
            "Prefix FK_ + Spine_01_JNT → FK_Spine_01_JNT"
        )

        self.baseLabel = QtWidgets.QLabel("Base Name")
        self.baseLabel.setToolTip(
            "The main name used in Sequential Rename mode.\n\n"
            "Hidden in Prefix / Suffix Only mode because that mode uses the current object name as the base."
        )

        self.baseEdit = QtWidgets.QLineEdit("Spine")
        self.baseEdit.setPlaceholderText("Base name, e.g. Spine, Arm, Chain")
        self.baseEdit.setToolTip(
            "Main name used for Sequential Rename.\n\n"
            "Example:\n"
            "Base Name Spine + padding 2 + suffix _JNT → Spine_01_JNT, Spine_02_JNT, Spine_03_JNT.\n\n"
            "Spaces and unsafe characters are cleaned into Maya-safe underscores."
        )

        self.suffixEdit = QtWidgets.QLineEdit("_JNT")
        self.suffixEdit.setPlaceholderText("Suffix, e.g. _JNT, _CTRL, _GEO")
        self.suffixEdit.setToolTip(
            "Text added after the generated name.\n\n"
            "Common examples:\n"
            "_JNT, _CTRL, _GEO, _LOC, _DRV"
        )

        self.numberChk = QtWidgets.QCheckBox("Enable Numbering")
        self.numberChk.setToolTip(
            "Turns sequential numbering on or off.\n\n"
            "On:\n"
            "Spine_01_JNT, Spine_02_JNT, Spine_03_JNT\n\n"
            "Off:\n"
            "Every enabled item previews the same base name, which may cause duplicate warnings unless the name is unique."
        )
        self.numberChk.setChecked(True)

        self.startSpin = QtWidgets.QSpinBox()
        self.startSpin.setRange(-999999, 999999)
        self.startSpin.setValue(1)
        self.startSpin.setFixedWidth(70)
        self.startSpin.setToolTip(
            "The number assigned to the first enabled item.\n\n"
            "Example:\n"
            "Start 1 → Spine_01_JNT\n"
            "Start 5 → Spine_05_JNT"
        )

        self.stepSpin = QtWidgets.QSpinBox()
        self.stepSpin.setRange(1, 999999)
        self.stepSpin.setValue(1)
        self.stepSpin.setFixedWidth(70)
        self.stepSpin.setToolTip(
            "How much the number increases per item.\n\n"
            "Example:\n"
            "Start 1, Step 1 → 01, 02, 03\n"
            "Start 1, Step 5 → 01, 06, 11"
        )

        self.paddingSpin = QtWidgets.QSpinBox()
        self.paddingSpin.setRange(1, 8)
        self.paddingSpin.setValue(2)
        self.paddingSpin.setFixedWidth(70)
        self.paddingSpin.setToolTip(
            "Minimum number of digits in the number.\n\n"
            "Padding 2 → 01, 02, 10\n"
            "Padding 3 → 001, 002, 010"
        )

        self.separatorEdit = QtWidgets.QLineEdit("_")
        self.separatorEdit.setFixedWidth(70)
        self.separatorEdit.setToolTip(
            "Text inserted between the base name and the number.\n\n"
            "Underscore is recommended for Maya rig names.\n\n"
            "Example:\n"
            "Base Spine + Separator _ + Number 01 + Suffix _JNT → Spine_01_JNT\n\n"
            "Using invalid Maya characters will be flagged in the preview instead of crashing."
        )

        self.numPositionCombo = QtWidgets.QComboBox()
        self.numPositionCombo.setToolTip(
            "Controls where the number is placed in Sequential Rename mode.\n\n"
            "After Base:\n"
            "Spine_01_JNT\n\n"
            "At End:\n"
            "Spine_JNT_01\n\n"
            "Before Base:\n"
            "01_Spine_JNT, or L_01_Spine_JNT if a prefix is used.\n"
            "Names that start with a number are invalid in Maya and will be flagged."
        )
        self.numPositionCombo.addItems(["After Base", "At End", "Before Base"])
        self.numPositionCombo.setCurrentText("After Base")

        self.searchEdit = QtWidgets.QLineEdit("")
        self.searchEdit.setPlaceholderText("Text or regex to search for")
        self.searchEdit.setToolTip(
            "Text to find in each current object name.\n\n"
            "Plain text by default.\n"
            "Enable Regex to treat this as a regular expression."
        )

        self.replaceEdit = QtWidgets.QLineEdit("")
        self.replaceEdit.setPlaceholderText("Replacement text")
        self.replaceEdit.setToolTip(
            "Text that replaces the search match.\n\n"
            "Leave empty to remove the matched text."
        )

        self.caseSensitiveChk = QtWidgets.QCheckBox("Case Sensitive")
        self.caseSensitiveChk.setToolTip(
            "When enabled, search text must match capitalization exactly.\n\n"
            "Example:\n"
            "Search arm will not match Arm if Case Sensitive is on."
        )

        self.regexChk = QtWidgets.QCheckBox("Regex")
        self.regexChk.setToolTip(
            "Treats Search For as a regular expression.\n\n"
            "Useful examples:\n"
            "^L_     matches L_ only at the start\n"
            "_JNT$   matches _JNT only at the end\n"
            "\\d+     matches one or more digits\n\n"
            "Invalid regex patterns are caught and shown in the preview status."
        )

        self.wholeWordChk = QtWidgets.QCheckBox("Whole Word")
        self.wholeWordChk.setToolTip(
            "Only replaces the search text when it appears as a separate word-like token.\n\n"
            "This is useful when you want Arm to match L_Arm_JNT but not ForeArm_JNT."
        )

        self.selectAllBtn = QtWidgets.QPushButton("Select All")
        self.selectAllBtn.setToolTip(
            "Enables all rows in the table so they are included in the rename."
        )

        self.deselectAllBtn = QtWidgets.QPushButton("Deselect All")
        self.deselectAllBtn.setToolTip(
            "Disables all rows in the table so none are included in the rename."
        )

        self.changesOnlyBtn = QtWidgets.QPushButton("Changes Only")
        self.changesOnlyBtn.setToolTip(
            "Enables only rows where the preview name is different from the current name and has no error."
        )

        self.copyPreviewBtn = QtWidgets.QPushButton("Copy Preview Names")
        self.copyPreviewBtn.setToolTip(
            "Copies all enabled, valid preview names to the clipboard.\n\n"
            "One name per line. Useful for checking, notes, or pasting into another tool."
        )

        self.previewBtn = QtWidgets.QPushButton("Refresh Preview")
        self.previewBtn.setToolTip(
            "Manually rebuilds the preview table.\n\n"
            "Most fields already update live, but this is useful after scene changes."
        )

        self.saveSettingsBtn = QtWidgets.QPushButton("Save Settings")
        self.saveSettingsBtn.setToolTip(
            "Saves the current rename settings to your Maya preferences folder.\n\n"
            "This includes mode, prefix, suffix, numbering options, and search/replace options."
        )

        self.loadSettingsBtn = QtWidgets.QPushButton("Load Settings")
        self.loadSettingsBtn.setToolTip(
            "Loads the previously saved rename settings from your Maya preferences folder."
        )

        self.renameBtn = QtWidgets.QPushButton("Apply Rename")
        self.renameBtn.setToolTip(
            "Applies the preview names to all enabled rows with no errors.\n\n"
            "The operation is wrapped in one Maya undo chunk, so one Undo should revert the full rename pass."
        )
        self.renameBtn.setStyleSheet(
            f"QPushButton {{ background-color: #3a5a3a; border-color: {C_ACCENT_GRN}; font-weight: bold; }}"
            f"QPushButton:hover {{ background-color: #4a6a4a; }}"
        )

        self.cancelBtn = QtWidgets.QPushButton("Close")
        self.cancelBtn.setToolTip(
            "Closes the tool window without changing the scene."
        )
        self.cancelBtn.setStyleSheet(
            f"QPushButton {{ background-color: #5a3030; border-color: {C_DANGER}; font-weight: bold; }}"
            f"QPushButton:hover {{ background-color: #6a3a3a; }}"
        )

    def _build_layout(self):
        main = QtWidgets.QVBoxLayout(self)
        main.setContentsMargins(8, 8, 8, 8)
        main.setSpacing(6)

        top = QtWidgets.QHBoxLayout()
        title = QtWidgets.QLabel("KS Maya Smart Rename")
        title.setStyleSheet("font-weight: bold; font-size: 13px;")
        title.setToolTip(
            "Bulk renaming tool for Maya scene objects.\n\n"
            "Works on joints, transforms, controls, locators, meshes, and most named Maya nodes."
        )
        top.addWidget(title)
        top.addStretch(1)
        top.addWidget(self.statusLbl)
        main.addLayout(top)

        splitter = QtWidgets.QSplitter(QtCore.Qt.Vertical)
        main.addWidget(splitter, 1)

        sel_sec = CollapsibleSection("Selection", expanded=True)

        row1 = QtWidgets.QHBoxLayout()
        row1.addWidget(self.loadSelBtn)
        row1.addWidget(self.addSelBtn)
        row1.addWidget(self.removeBtn)
        row1.addWidget(self.clearBtn)
        row1.addWidget(self.selectInMayaBtn)
        row1.addStretch(1)
        row1.addWidget(self.moveUpBtn)
        row1.addWidget(self.moveDownBtn)
        row1.addWidget(self.reverseBtn)

        sel_sec.addLayout(row1)
        sel_sec.addWidget(self.table, 1)
        splitter.addWidget(sel_sec)

        naming_sec = CollapsibleSection("Rename Settings", expanded=True)

        mode_row = QtWidgets.QHBoxLayout()
        mode_label = QtWidgets.QLabel("Mode")
        mode_label.setToolTip("Choose which rename operation mode to use.")
        mode_row.addWidget(mode_label)
        mode_row.addWidget(self.modeCombo, 1)
        mode_row.addWidget(self.saveSettingsBtn)
        mode_row.addWidget(self.loadSettingsBtn)
        naming_sec.addLayout(mode_row)

        self.seqWidget = QtWidgets.QWidget()
        seq_layout = QtWidgets.QVBoxLayout(self.seqWidget)
        seq_layout.setContentsMargins(0, 0, 0, 0)
        seq_layout.setSpacing(4)

        self.seqForm = QtWidgets.QGridLayout()
        self.seqForm.setColumnStretch(1, 1)

        prefix_label = QtWidgets.QLabel("Prefix")
        prefix_label.setToolTip(self.prefixEdit.toolTip())
        self.seqForm.addWidget(prefix_label, 0, 0)
        self.seqForm.addWidget(self.prefixEdit, 0, 1)

        self.seqForm.addWidget(self.baseLabel, 1, 0)
        self.seqForm.addWidget(self.baseEdit, 1, 1)

        suffix_label = QtWidgets.QLabel("Suffix")
        suffix_label.setToolTip(self.suffixEdit.toolTip())
        self.seqForm.addWidget(suffix_label, 2, 0)
        self.seqForm.addWidget(self.suffixEdit, 2, 1)

        seq_layout.addLayout(self.seqForm)

        self.numWidget = QtWidgets.QWidget()
        num_grid = QtWidgets.QGridLayout(self.numWidget)
        num_grid.setContentsMargins(0, 0, 0, 0)

        start_label = QtWidgets.QLabel("Start")
        start_label.setToolTip(self.startSpin.toolTip())

        step_label = QtWidgets.QLabel("Step")
        step_label.setToolTip(self.stepSpin.toolTip())

        padding_label = QtWidgets.QLabel("Padding")
        padding_label.setToolTip(self.paddingSpin.toolTip())

        separator_label = QtWidgets.QLabel("Separator")
        separator_label.setToolTip(self.separatorEdit.toolTip())

        number_position_label = QtWidgets.QLabel("Number Position")
        number_position_label.setToolTip(self.numPositionCombo.toolTip())

        num_grid.addWidget(self.numberChk, 0, 0, 1, 2)
        num_grid.addWidget(start_label, 1, 0)
        num_grid.addWidget(self.startSpin, 1, 1)
        num_grid.addWidget(step_label, 1, 2)
        num_grid.addWidget(self.stepSpin, 1, 3)
        num_grid.addWidget(padding_label, 1, 4)
        num_grid.addWidget(self.paddingSpin, 1, 5)
        num_grid.addWidget(separator_label, 1, 6)
        num_grid.addWidget(self.separatorEdit, 1, 7)
        num_grid.addWidget(number_position_label, 2, 0)
        num_grid.addWidget(self.numPositionCombo, 2, 1, 1, 3)
        num_grid.setColumnStretch(8, 1)

        seq_layout.addWidget(self.numWidget)

        self.searchReplaceWidget = QtWidgets.QWidget()
        sr_layout = QtWidgets.QVBoxLayout(self.searchReplaceWidget)
        sr_layout.setContentsMargins(0, 0, 0, 0)
        sr_layout.setSpacing(4)

        sr_form = QtWidgets.QGridLayout()
        sr_form.setColumnStretch(1, 1)

        search_label = QtWidgets.QLabel("Search For")
        search_label.setToolTip(self.searchEdit.toolTip())

        replace_label = QtWidgets.QLabel("Replace With")
        replace_label.setToolTip(self.replaceEdit.toolTip())

        sr_form.addWidget(search_label, 0, 0)
        sr_form.addWidget(self.searchEdit, 0, 1)
        sr_form.addWidget(replace_label, 1, 0)
        sr_form.addWidget(self.replaceEdit, 1, 1)
        sr_layout.addLayout(sr_form)

        sr_opts = QtWidgets.QHBoxLayout()
        sr_opts.addWidget(self.caseSensitiveChk)
        sr_opts.addWidget(self.regexChk)
        sr_opts.addWidget(self.wholeWordChk)
        sr_opts.addStretch(1)
        sr_layout.addLayout(sr_opts)

        naming_sec.addWidget(self.seqWidget)
        naming_sec.addWidget(self.searchReplaceWidget)

        action_row = QtWidgets.QHBoxLayout()
        action_row.addWidget(self.selectAllBtn)
        action_row.addWidget(self.deselectAllBtn)
        action_row.addWidget(self.changesOnlyBtn)
        action_row.addStretch(1)
        action_row.addWidget(self.copyPreviewBtn)
        action_row.addWidget(self.previewBtn)
        naming_sec.addLayout(action_row)

        splitter.addWidget(naming_sec)
        splitter.setSizes([450, 240])

        footer = QtWidgets.QHBoxLayout()
        footer.addStretch(1)
        footer.addWidget(self.renameBtn)
        footer.addWidget(self.cancelBtn)
        main.addLayout(footer)

    def _connect_signals(self):
        self.loadSelBtn.clicked.connect(self._load_current_selection)
        self.addSelBtn.clicked.connect(self._add_selected)
        self.removeBtn.clicked.connect(self._remove_selected_rows)
        self.clearBtn.clicked.connect(self._clear_items)
        self.selectInMayaBtn.clicked.connect(self._select_rows_in_maya)
        self.moveUpBtn.clicked.connect(lambda: self._move_selected(-1))
        self.moveDownBtn.clicked.connect(lambda: self._move_selected(1))
        self.reverseBtn.clicked.connect(self._reverse_items)

        self.modeCombo.currentIndexChanged.connect(self._on_mode_changed)

        for w in (self.prefixEdit, self.baseEdit, self.suffixEdit, self.separatorEdit, self.searchEdit, self.replaceEdit):
            w.textChanged.connect(self._refresh_preview)

        for w in (self.startSpin, self.stepSpin, self.paddingSpin):
            w.valueChanged.connect(self._refresh_preview)

        for w in (self.numberChk, self.caseSensitiveChk, self.regexChk, self.wholeWordChk):
            w.stateChanged.connect(self._refresh_preview)

        self.numPositionCombo.currentIndexChanged.connect(self._refresh_preview)
        self.table.itemChanged.connect(self._on_table_item_changed)

        self.selectAllBtn.clicked.connect(lambda: self._set_all_enabled(True))
        self.deselectAllBtn.clicked.connect(lambda: self._set_all_enabled(False))
        self.changesOnlyBtn.clicked.connect(self._select_changes_only)
        self.copyPreviewBtn.clicked.connect(self._copy_preview_names)
        self.previewBtn.clicked.connect(self._refresh_preview)

        self.saveSettingsBtn.clicked.connect(lambda: self._save_settings_to_prefs(silent=False))
        self.loadSettingsBtn.clicked.connect(lambda: self._load_settings_from_prefs(silent=False))

        self.renameBtn.clicked.connect(self._apply_rename)
        self.cancelBtn.clicked.connect(self.close)

    def _current_mode(self):
        return self.modeCombo.currentText()

    def _on_mode_changed(self):
        mode = self._current_mode()

        self.seqWidget.setVisible(mode in (MODE_SEQUENTIAL, MODE_PREFIX_SUFFIX))
        self.searchReplaceWidget.setVisible(mode == MODE_SEARCH_REPLACE)
        self.numWidget.setVisible(mode == MODE_SEQUENTIAL)

        show_base = mode == MODE_SEQUENTIAL
        self.baseLabel.setVisible(show_base)
        self.baseEdit.setVisible(show_base)
        self.baseEdit.setEnabled(show_base)

        self._refresh_preview()

    def _maya_selection(self):
        return cmds.ls(sl=True, long=True) or []

    def _load_current_selection(self):
        self._items = [RenameItem(p) for p in _unique_preserve_order(self._maya_selection())]
        self._refresh_preview()

    def _add_selected(self):
        existing = set(i.path for i in self._items)

        for p in self._maya_selection():
            if p not in existing:
                self._items.append(RenameItem(p))
                existing.add(p)

        self._refresh_preview()

    def _selected_rows(self):
        rows = sorted(set(i.row() for i in self.table.selectedIndexes()))
        return [r for r in rows if 0 <= r < len(self._items)]

    def _remove_selected_rows(self):
        for row in reversed(self._selected_rows()):
            self._items.pop(row)
        self._refresh_preview()

    def _clear_items(self):
        self._items = []
        self._refresh_preview()

    def _move_selected(self, delta):
        rows = self._selected_rows()
        if not rows:
            return

        iterable = rows if delta < 0 else reversed(rows)
        moved_rows = []

        for row in iterable:
            new_row = row + delta

            if new_row < 0 or new_row >= len(self._items):
                moved_rows.append(row)
                continue

            self._items[row], self._items[new_row] = self._items[new_row], self._items[row]
            moved_rows.append(new_row)

        self._refresh_preview()
        self.table.clearSelection()

        for row in moved_rows:
            if 0 <= row < len(self._items):
                self.table.selectRow(row)

    def _reverse_items(self):
        self._items.reverse()
        self._refresh_preview()

    def _set_all_enabled(self, state):
        for item in self._items:
            item.enabled = state
        self._refresh_preview()

    def _select_changes_only(self):
        self._compute_previews()

        for item in self._items:
            item.enabled = item.will_change and not item.error

        self._refresh_table()
        self._update_status()

    def _select_rows_in_maya(self):
        rows = self._selected_rows()
        paths = []

        if rows:
            for row in rows:
                item = self._items[row]
                if cmds.objExists(item.path):
                    paths.append(item.path)
        else:
            for item in self._items:
                if item.enabled and cmds.objExists(item.path):
                    paths.append(item.path)

        if paths:
            cmds.select(paths, replace=True)
        else:
            cmds.select(clear=True)

    def _build_sequential_name_for_index(self, index):
        prefix = self.prefixEdit.text() or ""
        base = _safe_name_piece(self.baseEdit.text())
        suffix = self.suffixEdit.text() or ""
        sep = self.separatorEdit.text() or ""

        if not base:
            return ""

        if not self.numberChk.isChecked():
            return f"{prefix}{base}{suffix}"

        value = self.startSpin.value() + (index * self.stepSpin.value())
        sign = "-" if value < 0 else ""
        num = sign + str(abs(value)).zfill(self.paddingSpin.value())
        pos = self.numPositionCombo.currentText()

        if pos == "After Base":
            return f"{prefix}{base}{sep}{num}{suffix}"
        if pos == "At End":
            return f"{prefix}{base}{suffix}{sep}{num}"
        if pos == "Before Base":
            return f"{prefix}{num}{sep}{base}{suffix}"

        return f"{prefix}{base}{sep}{num}{suffix}"

    def _build_search_replace_name(self, original):
        search = self.searchEdit.text() or ""
        replace = self.replaceEdit.text() or ""
        case_sensitive = self.caseSensitiveChk.isChecked()

        if not search:
            return original

        if self.regexChk.isChecked():
            flags = 0 if case_sensitive else re.IGNORECASE
            pattern = re.compile(search, flags)
            return pattern.sub(replace, original)

        if self.wholeWordChk.isChecked():
            return _replace_whole_word(original, search, replace, case_sensitive)

        return _replace_plain(original, search, replace, case_sensitive)

    def _build_prefix_suffix_name(self, original):
        prefix = self.prefixEdit.text() or ""
        suffix = self.suffixEdit.text() or ""
        return f"{prefix}{original}{suffix}"

    def _compute_previews(self):
        mode = self._current_mode()
        enabled_counter = 0
        generated = {}
        regex_error = None

        if mode == MODE_SEARCH_REPLACE and self.regexChk.isChecked():
            search = self.searchEdit.text() or ""
            if search:
                try:
                    flags = 0 if self.caseSensitiveChk.isChecked() else re.IGNORECASE
                    re.compile(search, flags)
                except Exception as exc:
                    regex_error = str(exc)

        for item in self._items:
            item.error = ""

            if not item.enabled:
                item.preview = item.original
                continue

            try:
                if mode == MODE_SEQUENTIAL:
                    item.preview = self._build_sequential_name_for_index(enabled_counter)
                    enabled_counter += 1
                elif mode == MODE_SEARCH_REPLACE:
                    if regex_error:
                        item.preview = item.original
                        item.error = "Regex error"
                    else:
                        item.preview = self._build_search_replace_name(item.original)
                elif mode == MODE_PREFIX_SUFFIX:
                    item.preview = self._build_prefix_suffix_name(item.original)
                else:
                    item.preview = item.original
            except Exception as exc:
                item.preview = item.original
                item.error = str(exc)

            if item.error:
                continue

            valid, reason = _is_valid_maya_short_name(item.preview)
            if not valid:
                item.error = reason
                continue

            if item.preview in generated and item.will_change:
                item.error = "Duplicate in batch"
                generated[item.preview].error = "Duplicate in batch"
            else:
                generated[item.preview] = item

        for item in self._items:
            if not item.enabled or item.error or not item.will_change:
                continue

            try:
                matches = cmds.ls(item.preview) or []
            except Exception:
                matches = []

            if matches:
                item.error = "Name exists in scene"

    def _refresh_preview(self):
        self._compute_previews()
        self._refresh_table()
        self._update_status()

    def _refresh_table(self):
        self._building_table = True

        try:
            self.table.setRowCount(len(self._items))

            for row, item in enumerate(self._items):
                chk = QtWidgets.QTableWidgetItem("")
                chk.setFlags(
                    QtCore.Qt.ItemIsEnabled |
                    QtCore.Qt.ItemIsUserCheckable |
                    QtCore.Qt.ItemIsSelectable
                )
                chk.setCheckState(QtCore.Qt.Checked if item.enabled else QtCore.Qt.Unchecked)
                chk.setToolTip(
                    "Checked rows are included when Apply Rename is pressed.\n"
                    "Unchecked rows are left unchanged."
                )
                self.table.setItem(row, 0, chk)

                typ = QtWidgets.QTableWidgetItem(_node_type(item.path))
                typ.setFlags(QtCore.Qt.ItemIsEnabled | QtCore.Qt.ItemIsSelectable)
                typ.setToolTip("Maya node type for this object.")
                self.table.setItem(row, 1, typ)

                cur = QtWidgets.QTableWidgetItem(item.original)
                cur.setFlags(QtCore.Qt.ItemIsEnabled | QtCore.Qt.ItemIsSelectable)
                cur.setToolTip(
                    f"Current short name:\n{item.original}\n\nFull path:\n{item.path}"
                )
                self.table.setItem(row, 2, cur)

                arrow = QtWidgets.QTableWidgetItem("→" if item.will_change else "")
                arrow.setTextAlignment(QtCore.Qt.AlignCenter)
                arrow.setFlags(QtCore.Qt.ItemIsEnabled | QtCore.Qt.ItemIsSelectable)
                arrow.setToolTip("Shows that the current name will change to the preview name.")
                self.table.setItem(row, 3, arrow)

                prev = QtWidgets.QTableWidgetItem(item.preview)
                prev.setFlags(QtCore.Qt.ItemIsEnabled | QtCore.Qt.ItemIsSelectable)

                if item.error:
                    prev.setForeground(QtGui.QBrush(QtGui.QColor(C_DANGER)))
                    prev.setToolTip(
                        f"Preview name has an issue:\n{item.error}\n\n"
                        "Fix the settings or disable this row before applying."
                    )
                elif item.will_change:
                    prev.setForeground(QtGui.QBrush(QtGui.QColor(C_COUNT_LBL)))
                    prev.setToolTip(
                        f"This row will be renamed to:\n{item.preview}"
                    )
                else:
                    prev.setForeground(QtGui.QBrush(QtGui.QColor(C_TEXT_DIM)))
                    prev.setToolTip(
                        "Preview name matches the current name, so this row will not change."
                    )

                self.table.setItem(row, 4, prev)

                status_text = item.error if item.error else (
                    "Will rename" if item.enabled and item.will_change else ""
                )

                status = QtWidgets.QTableWidgetItem(status_text)
                status.setFlags(QtCore.Qt.ItemIsEnabled | QtCore.Qt.ItemIsSelectable)

                if item.error:
                    status.setForeground(QtGui.QBrush(QtGui.QColor(C_DANGER)))
                    status.setToolTip(
                        "This row cannot be renamed until the error is fixed."
                    )
                elif item.enabled and item.will_change:
                    status.setToolTip(
                        "This row is enabled and ready to be renamed."
                    )
                else:
                    status.setToolTip(
                        "This row is either disabled or does not currently produce a name change."
                    )

                self.table.setItem(row, 5, status)

        finally:
            self._building_table = False

    def _on_table_item_changed(self, qitem):
        if self._building_table or qitem.column() != 0:
            return

        row = qitem.row()

        if 0 <= row < len(self._items):
            self._items[row].enabled = qitem.checkState() == QtCore.Qt.Checked
            self._refresh_preview()

    def _update_status(self):
        count = len(self._items)
        rename_count = sum(1 for i in self._items if i.enabled and i.will_change and not i.error)
        err_count = sum(1 for i in self._items if i.enabled and i.error)

        msg = f"Items: {count} | Will rename: {rename_count}"
        if err_count:
            msg += f" | Errors: {err_count}"

        self.statusLbl.setText(msg)
        self.renameBtn.setEnabled(rename_count > 0 and err_count == 0)

    def _copy_preview_names(self):
        self._compute_previews()
        names = [
            item.preview
            for item in self._items
            if item.enabled and item.will_change and not item.error
        ]

        if not names:
            QtWidgets.QMessageBox.information(self, WINDOW_TITLE, "No preview names to copy.")
            return

        QtWidgets.QApplication.clipboard().setText("\n".join(names))
        QtWidgets.QMessageBox.information(self, WINDOW_TITLE, f"Copied {len(names)} preview name(s).")

    def _settings_data(self):
        return {
            "mode": self.modeCombo.currentText(),
            "prefix": self.prefixEdit.text(),
            "base": self.baseEdit.text(),
            "suffix": self.suffixEdit.text(),
            "number_enabled": self.numberChk.isChecked(),
            "start": self.startSpin.value(),
            "step": self.stepSpin.value(),
            "padding": self.paddingSpin.value(),
            "separator": self.separatorEdit.text(),
            "number_position": self.numPositionCombo.currentText(),
            "search": self.searchEdit.text(),
            "replace": self.replaceEdit.text(),
            "case_sensitive": self.caseSensitiveChk.isChecked(),
            "regex": self.regexChk.isChecked(),
            "whole_word": self.wholeWordChk.isChecked(),
        }

    def _apply_settings_data(self, data):
        if not data:
            return

        mode = data.get("mode", MODE_SEQUENTIAL)
        idx = self.modeCombo.findText(mode)
        if idx >= 0:
            self.modeCombo.setCurrentIndex(idx)

        self.prefixEdit.setText(data.get("prefix", ""))
        self.baseEdit.setText(data.get("base", "Spine"))
        self.suffixEdit.setText(data.get("suffix", "_JNT"))
        self.numberChk.setChecked(bool(data.get("number_enabled", True)))
        self.startSpin.setValue(int(data.get("start", 1)))
        self.stepSpin.setValue(int(data.get("step", 1)))
        self.paddingSpin.setValue(int(data.get("padding", 2)))
        self.separatorEdit.setText(data.get("separator", "_"))

        num_pos = data.get("number_position", "After Base")
        idx = self.numPositionCombo.findText(num_pos)
        if idx >= 0:
            self.numPositionCombo.setCurrentIndex(idx)

        self.searchEdit.setText(data.get("search", ""))
        self.replaceEdit.setText(data.get("replace", ""))
        self.caseSensitiveChk.setChecked(bool(data.get("case_sensitive", False)))
        self.regexChk.setChecked(bool(data.get("regex", False)))
        self.wholeWordChk.setChecked(bool(data.get("whole_word", False)))

        self._on_mode_changed()
        self._refresh_preview()

    def _save_settings_to_prefs(self, silent=False):
        ok = _save_json(_prefs_path(), self._settings_data())

        if not silent:
            if ok:
                QtWidgets.QMessageBox.information(self, WINDOW_TITLE, "Settings saved.")
            else:
                QtWidgets.QMessageBox.warning(self, WINDOW_TITLE, "Failed to save settings.")

    def _load_settings_from_prefs(self, silent=False):
        data = _load_json(_prefs_path())

        if not data:
            if not silent:
                QtWidgets.QMessageBox.information(self, WINDOW_TITLE, "No saved settings found.")
            return

        self._apply_settings_data(data)

        if not silent:
            QtWidgets.QMessageBox.information(self, WINDOW_TITLE, "Settings loaded.")

    def _apply_rename(self):
        self._compute_previews()

        targets = [i for i in self._items if i.enabled and i.will_change and not i.error]
        errors = [i for i in self._items if i.enabled and i.error]

        if errors:
            QtWidgets.QMessageBox.warning(self, WINDOW_TITLE, "Fix preview errors before renaming.")
            self._refresh_preview()
            return

        if not targets:
            QtWidgets.QMessageBox.information(self, WINDOW_TITLE, "Nothing to rename.")
            return

        ok = QtWidgets.QMessageBox.question(
            self,
            WINDOW_TITLE,
            f"Rename {len(targets)} item(s)?\n\nThis is undoable with one Maya undo step.",
            QtWidgets.QMessageBox.Yes | QtWidgets.QMessageBox.No,
            QtWidgets.QMessageBox.No,
        )

        if ok != QtWidgets.QMessageBox.Yes:
            return

        renamed = 0
        failed = []

        cmds.undoInfo(openChunk=True, chunkName="KS Maya Smart Rename")

        try:
            targets = sorted(targets, key=lambda x: len(x.path.split("|")), reverse=True)

            for item in targets:
                try:
                    if not cmds.objExists(item.path):
                        raise RuntimeError("Object no longer exists")

                    new_path = cmds.rename(item.path, item.preview)
                    item.path = new_path
                    item.original = _short_name(new_path)
                    renamed += 1

                except Exception as exc:
                    failed.append(f"{item.original} → {item.preview}: {exc}")

        finally:
            try:
                cmds.undoInfo(closeChunk=True)
            except Exception:
                pass

        self._refresh_preview()

        if failed:
            QtWidgets.QMessageBox.warning(
                self,
                WINDOW_TITLE,
                f"Renamed {renamed} item(s).\n{len(failed)} failed:\n\n" + "\n".join(failed[:8]),
            )
        else:
            QtWidgets.QMessageBox.information(self, WINDOW_TITLE, f"Renamed {renamed} item(s).")


def _get_maya_window():
    try:
        import maya.OpenMayaUI as omui

        try:
            import shiboken2 as shiboken
        except Exception:
            import shiboken6 as shiboken

    except Exception:
        return None

    ptr = omui.MQtUtil.mainWindow()

    if ptr is None:
        return None

    return shiboken.wrapInstance(int(ptr), QtWidgets.QWidget)


_dialog = None


def show():
    global _dialog

    try:
        if _dialog is not None:
            try:
                _dialog.close()
                _dialog.deleteLater()
            except Exception:
                pass

        _dialog = KSMayaSmartRenameUI(parent=_get_maya_window())

    except Exception:
        _dialog = KSMayaSmartRenameUI()

    _dialog.show()
    _dialog.raise_()
    _dialog.activateWindow()

    return _dialog


if __name__ == "__main__":
    show()