#!/usr/bin/env python3
"""Writes the small SpreadsheetML inputs that LibreOffice converts to legacy .xls files for the XLS reader tests.

Usage: make_xls_inputs.py <output directory>
Everything is generated from fixed values, so the inputs are the same on every run.
"""
import sys
import os
from datetime import datetime

from openpyxl import Workbook
from openpyxl.comments import Comment
from openpyxl.formatting.rule import CellIsRule, FormulaRule
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side, Protection
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.workbook.defined_name import DefinedName


def basic(path):
    wb = Workbook()
    ws = wb.active
    ws.title = "Data"
    ws["A1"] = "Name"
    ws["B1"] = "Qty"
    ws["C1"] = "Price"
    ws["D1"] = "Total"
    rows = [("Apple", 3, 1.25), ("Pear", 10, 0.5), ("Fig", 0, 12.75), ("Café ünïcode ✓", 7, 3)]
    for i, (n, q, p) in enumerate(rows, start=2):
        ws.cell(i, 1, n)
        ws.cell(i, 2, q)
        ws.cell(i, 3, p)
        ws.cell(i, 4, f"=B{i}*C{i}")
    ws["A7"] = "Sum"
    ws["D7"] = "=SUM(D2:D5)"
    ws["A8"] = "Flag"
    ws["B8"] = True
    ws["C8"] = "=B8"
    ws["A9"] = "Error"
    ws["B9"] = "=1/0"
    ws["A10"] = "Date"
    ws["B10"] = datetime(2024, 2, 29, 13, 30)
    ws["B10"].number_format = "yyyy-mm-dd hh:mm"
    ws["A11"] = "Text formula"
    ws["B11"] = '=CONCATENATE(A2,"-",A3)'
    ws["A12"] = "Big"
    ws["B12"] = 123456789.123
    ws["B12"].number_format = "#,##0.00"
    ws["C12"] = 0.256
    ws["C12"].number_format = "0.0%"
    ws["A13"] = "Neg"
    ws["B13"] = -42
    ws["C13"] = 1e-10
    ws["D13"] = 65536 * 3
    ws["A14"] = "Small int"
    ws["B14"] = 5
    ws["C14"] = 536870912
    ws["D14"] = 1073741824.5
    wb.save(path)


def formulas(path):
    wb = Workbook()
    ws = wb.active
    ws.title = "F"
    ws2 = wb.create_sheet("Other Sheet")
    ws2["A1"] = 10
    ws2["A2"] = 20
    ws2["B1"] = "x"
    for i in range(1, 11):
        ws.cell(i, 1, i)
        ws.cell(i, 2, i * i)
    items = [
        "=A1+B1*2-3/4",
        "=(A1+B1)*2",
        "=-A1^2",
        "=A1&\"-\"&B1",
        "=SUM(A1:A10)",
        "=AVERAGE(A1:B10)",
        "=IF(A1>5,\"big\",\"small\")",
        "=IF(AND(A1>1,B1<100),1,0)",
        "=ROUND(A1/3,2)",
        "=VLOOKUP(3,A1:B10,2,FALSE)",
        "=INDEX(A1:B10,2,2)",
        "=MATCH(4,A1:A10,0)",
        "='Other Sheet'!A1+'Other Sheet'!A2",
        "=SUM('Other Sheet'!A1:A2)",
        "=$A$1+A$2+$A3",
        "=A1:A3 B2:B4",
        "=SUM(A1:A3,B1:B3)",
        "=COUNTIF(A1:A10,\">5\")",
        "=SUMIF(A1:A10,\">5\",B1:B10)",
        "=TEXT(A1,\"0.00\")",
        "=LEN(\"hello\")",
        "=MID(\"abcdef\",2,3)",
        "=NOW()-TODAY()",
        "=A1%",
        "=PI()*2",
        "=MAX(A1:A10)-MIN(B1:B10)",
        "=IFERROR(1/0,\"err\")",
        "=SUMPRODUCT(A1:A10,B1:B10)",
        "=CHOOSE(2,\"a\",\"b\",\"c\")",
        "=ISNUMBER(A1)",
        "=A:A",
        "=SUM(A:A)",
        "=SUM(1:1)",
        "=NOT(TRUE)",
        "=\"quote\"\"d\"",
        "=1E+21+0.5",
        "=SQRT(16)",
        "=DATE(2020,2,29)",
        "=UPPER(\"x\")&LOWER(\"Y\")",
        "=COUNTA(A1:B10)",
        "=SUBSTITUTE(\"aXb\",\"X\",\"-\")",
        "=ROW()+COLUMN()",
        "=OFFSET(A1,1,1)",
        "=INDIRECT(\"A1\")",
        "=Total*2",
        "=LARGE(A1:A10,2)",
    ]
    for i, f in enumerate(items, start=1):
        ws.cell(i, 4, f)
    # shared formula (filled down)
    for i in range(1, 11):
        ws.cell(i, 6, f"=A{i}*2+B{i}")
    # array formula
    ws["H1"] = 1
    ws["H2"] = 2
    ws["H3"] = 3
    ws.formula_attributes = {}
    from openpyxl.worksheet.formula import ArrayFormula
    ws["J1"] = ArrayFormula("J1:J3", "=H1:H3*2")
    ws["L1"] = ArrayFormula("L1", "=SUM(H1:H3*H1:H3)")
    wb.defined_names["Total"] = DefinedName("Total", attr_text="F!$A$1")
    wb.defined_names["Rng"] = DefinedName("Rng", attr_text="F!$A$1:$B$10")
    ws2.defined_names["LocalName"] = DefinedName("LocalName", attr_text="'Other Sheet'!$A$1")
    wb.save(path)


def styles(path):
    wb = Workbook()
    ws = wb.active
    ws.title = "Styles"
    thin = Side(style="thin", color="FF0000")
    ws["A1"] = "bold italic red"
    ws["A1"].font = Font(name="Arial", size=14, bold=True, italic=True, color="FF0000", underline="single", strike=True)
    ws["A2"] = "fill"
    ws["A2"].fill = PatternFill("solid", fgColor="FFFF00")
    ws["A3"] = "borders"
    ws["A3"].border = Border(left=thin, right=Side(style="thick", color="0000FF"), top=Side(style="double"), bottom=Side(style="dashed"))
    ws["A4"] = "center wrap long text that wraps around"
    ws["A4"].alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
    ws["A5"] = "rotated"
    ws["A5"].alignment = Alignment(text_rotation=45)
    ws["A6"] = "indent"
    ws["A6"].alignment = Alignment(horizontal="left", indent=2)
    ws["A7"] = "shrink"
    ws["A7"].alignment = Alignment(shrink_to_fit=True)
    ws["A8"] = "locked off"
    ws["A8"].protection = Protection(locked=False, hidden=True)
    ws["A9"] = 1234.5
    ws["A9"].number_format = '"$"#,##0.00_);[Red]("$"#,##0.00)'
    ws["A10"] = 0.5
    ws["A10"].number_format = "# ?/?"
    ws["A11"] = 45000
    ws["A11"].number_format = "[$-409]d-mmm-yy;@"
    ws["A12"] = "pattern"
    ws["A12"].fill = PatternFill("lightGrid", fgColor="00FF00", bgColor="FF0000")
    ws.column_dimensions["A"].width = 30
    ws.column_dimensions["B"].width = 12
    ws.column_dimensions["C"].hidden = True
    ws.row_dimensions[4].height = 45
    ws.row_dimensions[6].hidden = True
    ws.merge_cells("B1:D2")
    ws.merge_cells("B5:C6")
    ws.freeze_panes = "B2"
    ws.sheet_view.showGridLines = False
    ws.sheet_view.zoomScale = 150
    ws.sheet_properties.tabColor = "FF0000"
    wb.save(path)


def features(path):
    wb = Workbook()
    ws = wb.active
    ws.title = "Feat"
    for i in range(1, 11):
        ws.cell(i, 1, i)
    ws["C1"] = "link"
    ws["C1"].hyperlink = "https://example.com/a?b=1&c=2"
    ws["C2"] = "internal"
    ws["C2"].hyperlink = "#Feat!A5"
    ws["C3"] = "note"
    ws["C3"].comment = Comment("This is a comment\nwith two lines", "Tester")
    ws["C4"] = "note2"
    ws["C4"].comment = Comment("Second comment", "Other Author")
    dv = DataValidation(type="list", formula1='"red,green,blue"', allow_blank=True)
    dv.prompt = "pick one"
    dv.promptTitle = "Colour"
    dv.error = "no good"
    dv.errorTitle = "Bad"
    ws.add_data_validation(dv)
    dv.add("E1:E5")
    dv2 = DataValidation(type="whole", operator="between", formula1="1", formula2="10")
    ws.add_data_validation(dv2)
    dv2.add("F1:F3")
    dv3 = DataValidation(type="list", formula1="=$A$1:$A$10")
    ws.add_data_validation(dv3)
    dv3.add("G1")
    ws.conditional_formatting.add("A1:A10", CellIsRule(operator="greaterThan", formula=["5"], fill=PatternFill("solid", bgColor="FF9999", fgColor="FF9999"), font=Font(bold=True, color="FF0000")))
    ws.conditional_formatting.add("A1:A10", CellIsRule(operator="between", formula=["2", "4"], fill=PatternFill("solid", bgColor="99FF99", fgColor="99FF99")))
    ws.conditional_formatting.add("B1:B10", FormulaRule(formula=["MOD(A1,2)=0"], font=Font(italic=True)))
    ws.print_area = "A1:G10"
    ws.print_title_rows = "1:2"
    ws.page_setup.orientation = "landscape"
    ws.page_setup.paperSize = 9
    ws.page_setup.fitToWidth = 1
    ws.page_setup.fitToHeight = 0
    ws.sheet_properties.pageSetUpPr.fitToPage = True
    ws.page_margins.left = 0.5
    ws.page_margins.top = 1.25
    ws.oddHeader.center.text = "Header text"
    ws.oddFooter.right.text = "Page &P"
    ws.print_options.gridLines = True
    ws.print_options.horizontalCentered = True
    from openpyxl.worksheet.pagebreak import Break
    ws.row_breaks.append(Break(id=5))
    ws.col_breaks.append(Break(id=3))
    ws.auto_filter.ref = "A1:B10"
    ws.protection.sheet = True
    ws.protection.password = "abc"
    ws2 = wb.create_sheet("Hidden")
    ws2["A1"] = "h"
    ws2.sheet_state = "hidden"
    ws3 = wb.create_sheet("Group")
    for i in range(1, 8):
        ws3.cell(i, 1, i)
    for i in (2, 3, 4):
        ws3.row_dimensions[i].outlineLevel = 1
    ws3.row_dimensions[5].collapsed = True
    ws3.column_dimensions.group("B", "D", outline_level=1)
    wb.save(path)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    basic(os.path.join(out, "basic.xlsx"))
    formulas(os.path.join(out, "formulas.xlsx"))
    styles(os.path.join(out, "styles.xlsx"))
    features(os.path.join(out, "features.xlsx"))


main()
