"""Writes the workbooks that the oracle (a spreadsheet application run headless) converts to ODS for the ODS reader tests.

Usage: make_inputs.py OUTDIR   (needs openpyxl; run from a venv)
"""
import datetime
import sys
from openpyxl import Workbook
from openpyxl.comments import Comment
from openpyxl.formatting.rule import CellIsRule, ColorScaleRule, DataBarRule, FormulaRule, IconSetRule
from openpyxl.styles import Alignment, Border, Font, PatternFill, Protection, Side
from openpyxl.workbook.defined_name import DefinedName
from openpyxl.worksheet.datavalidation import DataValidation

out = sys.argv[1]


def rich():
    wb = Workbook()
    ws = wb.active
    ws.title = 'Values'
    ws['A1'] = 'Text'
    ws['B1'] = 1234.5
    ws['C1'] = True
    ws['D1'] = datetime.datetime(2024, 3, 15, 13, 45, 30)
    ws['E1'] = datetime.time(8, 30)
    ws['F1'] = 0.256
    ws['G1'] = -42
    ws['H1'] = '=B1*2'
    ws['I1'] = '=A1&"!"'
    ws['J1'] = '=SUM(B1:G1)'
    ws['K1'] = '=IF(C1,"yes","no")'
    ws['L1'] = '=Other!A1+1'
    ws['M1'] = '=Total*2'
    ws['N1'] = '=#N/A'
    ws['O1'] = datetime.date(2024, 12, 31)
    ws['B2'] = 1234.5
    ws['B2'].number_format = '#,##0.00'
    ws['C2'] = 0.256
    ws['C2'].number_format = '0.0%'
    ws['D2'] = 1234.5
    ws['D2'].number_format = '"$"#,##0.00'
    ws['E2'] = 1234.5
    ws['E2'].number_format = '0.00E+00'
    ws['F2'] = datetime.datetime(2024, 3, 15)
    ws['F2'].number_format = 'dd/mm/yyyy'
    ws['G2'] = datetime.datetime(2024, 3, 15)
    ws['G2'].number_format = 'dddd, mmmm d, yyyy'
    ws['H2'] = 0.5
    ws['H2'].number_format = '# ?/?'
    ws['I2'] = 5
    ws['I2'].number_format = '[Red]0.00;[Blue]-0.00;"zero"'
    ws['J2'] = 'abc'
    ws['J2'].number_format = '@'
    ws['K2'] = datetime.time(13, 5, 9)
    ws['K2'].number_format = 'h:mm:ss AM/PM'
    ws['L2'] = 1.5
    ws['L2'].number_format = '[>1]"big"0.0;"small"0.0'
    ws['A3'] = 'Bold red on yellow'
    ws['A3'].font = Font(bold=True, italic=True, color='FFFF0000', size=14, name='Arial', underline='single', strike=True)
    ws['A3'].fill = PatternFill('solid', fgColor='FFFFFF00')
    ws['A3'].alignment = Alignment(horizontal='center', vertical='top', wrap_text=True, indent=1)
    thin = Side(style='thin', color='FF0000FF')
    ws['B3'] = 'boxed'
    ws['B3'].border = Border(left=thin, right=Side(style='double'), top=Side(style='thick', color='FF00FF00'), bottom=Side(style='dashed'))
    ws['C3'] = 'locked off'
    ws['C3'].protection = Protection(locked=False, hidden=True)
    ws['D3'] = 'rotated'
    ws['D3'].alignment = Alignment(text_rotation=45, shrink_to_fit=True)
    ws.merge_cells('A5:C6')
    ws['A5'] = 'merged'
    ws.merge_cells('E5:E8')
    ws['A8'] = 'hidden row below'
    ws.row_dimensions[9].hidden = True
    ws['A9'] = 'hidden'
    ws.row_dimensions[10].height = 30
    ws['A10'] = 'tall'
    ws.column_dimensions['A'].width = 30
    ws.column_dimensions['C'].hidden = True
    ws.column_dimensions['D'].width = 5
    ws['A11'] = 'with comment'
    ws['A11'].comment = Comment('Remember this', 'Author One')
    ws['A12'] = 'link'
    ws['A12'].hyperlink = 'https://example.com/a?b=1&c=2'
    ws['A13'] = 'inner link'
    ws['A13'].hyperlink = '#Other!B2'
    ws['A14'] = 'line1\nline2'
    ws['A14'].alignment = Alignment(wrap_text=True)
    ws['A15'] = '  spaced   text  '
    ws['A16'] = 'tab\there'
    ws.freeze_panes = 'B2'
    ws.auto_filter.ref = 'A20:C25'
    for r in range(20, 26):
        for c in range(1, 4):
            ws.cell(r, c, r * c)
    ws.print_area = 'A1:F30'
    ws.print_title_rows = '1:2'
    ws.page_setup.orientation = 'landscape'
    ws.page_setup.paperSize = 9
    ws.page_setup.scale = 80
    ws.page_margins.left = 0.5
    ws.oddHeader.center.text = 'Header text'
    ws.oddFooter.center.text = 'Page &P'
    ws.sheet_properties.tabColor = 'FF00FF00'
    ws.sheet_view.showGridLines = False
    dv = DataValidation(type='list', formula1='"red,green,blue"', allow_blank=True)
    dv.error = 'Pick a colour'
    dv.errorTitle = 'Bad'
    dv.prompt = 'Choose'
    dv.promptTitle = 'Colour'
    ws.add_data_validation(dv)
    dv.add('A30:A35')
    dv2 = DataValidation(type='whole', operator='between', formula1='1', formula2='10')
    ws.add_data_validation(dv2)
    dv2.add('B30:B35')
    dv3 = DataValidation(type='decimal', operator='greaterThan', formula1='B1')
    ws.add_data_validation(dv3)
    dv3.add('C30')
    dv4 = DataValidation(type='list', formula1='$A$20:$A$25')
    ws.add_data_validation(dv4)
    dv4.add('D30')
    red = PatternFill(bgColor='FFFFC7CE', fill_type='solid')
    ws.conditional_formatting.add('B20:B25', CellIsRule(operator='greaterThan', formula=['50'], fill=red, font=Font(color='FF9C0006')))
    ws.conditional_formatting.add('C20:C25', CellIsRule(operator='between', formula=['10', '60'], fill=red))
    ws.conditional_formatting.add('A20:A25', FormulaRule(formula=['MOD(A20,40)=0'], fill=red))
    ws.conditional_formatting.add('B40:B45', ColorScaleRule(start_type='min', start_color='FFF8696B', end_type='max', end_color='FF63BE7B'))
    ws.conditional_formatting.add('C40:C45', ColorScaleRule(start_type='min', start_color='FFFFFFFF', mid_type='percentile', mid_value=50, mid_color='FFFFEB84', end_type='max', end_color='FF63BE7B'))
    ws.conditional_formatting.add('D40:D45', DataBarRule(start_type='min', end_type='max', color='FF638EC6'))
    ws.conditional_formatting.add('E40:E45', IconSetRule('3TrafficLights1', 'percent', [0, 33, 67]))
    for r in range(40, 46):
        for c in range(2, 6):
            ws.cell(r, c, r * c % 17)
    other = wb.create_sheet('Other')
    other['A1'] = 41
    other['B2'] = 'target'
    other.sheet_state = 'hidden'
    other.protection.sheet = True
    other.protection.formatCells = False
    third = wb.create_sheet('My Sheet')
    third['A1'] = '=Values!B1+Other!A1'
    third['A2'] = '=SUM(Values!B20:B25)'
    third['A3'] = '=SUMIF(Values!A20:A25,">30")'
    third['A4'] = '=VLOOKUP(40,Values!A20:C25,2,FALSE)'
    third['A5'] = '=TEXT(Values!D1,"yyyy-mm-dd")'
    third['A6'] = '=INDEX(Values!A20:C25,2,2)'
    third['A7'] = '=SUMPRODUCT(Values!A20:A25,Values!B20:B25)'
    third['B1'] = '=A1+1'
    third['C1'] = '=IFERROR(1/0,"err")'
    third['C2'] = '=1/0'
    third['A8'] = '=SUM({1,2;3,4})'
    third.formula_attributes = {}
    wb.defined_names['Total'] = DefinedName('Total', attr_text='Values!$B$20:$B$25')
    wb.defined_names['Rate'] = DefinedName('Rate', attr_text='0.07')
    third.defined_names['Local'] = DefinedName('Local', attr_text="'My Sheet'!$A$1")
    wb.properties.title = 'Rich ODS source'
    wb.properties.creator = 'Tester'
    wb.properties.keywords = 'a,b'
    wb.save(out + '/ods-rich.xlsx')


def big():
    wb = Workbook()
    ws = wb.active
    ws.title = 'Data'
    for r in range(1, 2001):
        ws.cell(r, 1, r)
        ws.cell(r, 2, f'row {r}')
        ws.cell(r, 3, r * 0.5)
        ws.cell(r, 4, f'=A{r}*C{r}')
    wb.save(out + '/ods-big.xlsx')


rich()
big()
