#!/usr/bin/env python3
"""Writes the CSV files used to compare OneDocument's reading of delimited text with Excel's.

    make-grid.py OUTDIR

values.csv      one case per line: `id,<raw field>`; the raw field is written exactly as listed (quoted or not).
struct-*.csv    shapes: quoting, line breaks, ragged rows, encodings.
Everything is generated here, nothing is taken from a corpus.
"""
import os
import sys

out = sys.argv[1]
os.makedirs(out, exist_ok=True)

values = []


def add(*raws):
    values.extend(raws)


# integers, decimals, signs
add("0", "1", "-1", "+1", "42", "007", "-007", "+007", "0.5", "00.5", "-0", "0.0", "1.", ".5", "-.5", "+.5", "5.", "1.50", "100", "1000000")
add("1234567890", "123456789012345", "1234567890123456", "12345678901234567890", "0.1234567890123456", "123456789.123456789", "1.7976931348623157E+308", "1E309", "4.9E-324", "1E-400")
# grouping
add("1,234", "1,234,567", "1,234.5", "12,34", "1,23", "1,2345", "1,234,56", ",123", "123,", "1,,234", "1 234", "1, 234", "0,123", "-1,234", "(1,234)", "1,234-")
# currency, parentheses, trailing sign
add("$5", "$5.00", "$1,234.50", "-$5", "$-5", "($5)", "5$", "$ 5", "(5)", "(5", "5)", "(-5)", "5-", "5+", "- 5", "+ 5", "--5", "+-5", "€5", "£5", "\u00a55")
# exponents
add("1E3", "1e3", "1E+3", "1E-3", "1.5E3", "1E", "E3", "1E3.5", "1e03", "1E0003", "1e30", "-1E3", "1,000E3", "1E3%")
# percents
add("5%", "5.5%", "-5%", "(5%)", "5 %", "%5", "5%%", "0%", "100%", "0.5%", "1,000%", "%")
# booleans
add("TRUE", "FALSE", "true", "false", "True", "False", "tRuE", "T", "F", "TRUE ", " TRUE", "YES", "NO", "1=1")
# errors
add("#N/A", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#NULL!", "#n/a", "#SPILL!", "#CALC!", "#GETTING_DATA", "#NA", "#")
# dates
add("1/2/2020", "01/02/2020", "1/2/20", "12/31/1999", "2/29/2020", "2/29/2019", "2/30/2020", "13/1/2020", "1/13/2020", "0/1/2020", "1/0/2020", "1/1/1900", "12/31/1899", "1/1/1899", "2/29/1900", "3/1/1900", "12/31/9999", "1/1/10000", "1/1/0001")
add("2020-01-02", "2020-1-2", "2020/01/02", "2020.01.02", "20200102", "2020-13-01", "2020-02-30", "99-01-02", "00-01-02", "29-1-2", "30-1-2")
add("1-2-2020", "1-2-20", "1.2.2020", "1.2.20", "1/2", "1-2", "1/2/", "/1/2", "1//2", "1/2/2020/3")
add("2-Jan-2020", "02-Jan-2020", "2-Jan-20", "2-JAN-2020", "2 Jan 2020", "2 January 2020", "Jan 2, 2020", "Jan 2 2020", "January 2, 2020", "Jan-2-2020", "Jan 2020", "January 2020", "Jan-2020", "Jan-20", "2-Jan", "Jan 2", "Jan", "January", "Sept 2, 2020", "Sep 2, 2020", "Monday, January 2, 2020", "Mon Jan 2 2020", "2 Jan, 2020")
add("1/2/2020 13:45", "1/2/2020 1:45 PM", "1/2/2020 1:45:30 PM", "2020-01-02 13:45", "2020-01-02 13:45:30", "2020-01-02T13:45:30", "2020-01-02T13:45:30Z", "2020-01-02T13:45:30.5", "2020-01-02 13:45:30.123", "2020-01-02 25:00", "2020-01-02 24:00", "2020-01-02 13:60")
# times
add("13:45", "1:45", "01:45", "1:45 PM", "1:45PM", "1:45 pm", "1:45 P", "12:00 AM", "12:00 PM", "0:00", "24:00", "23:59:59", "23:59:60", "13:45:30", "13:45:30.5", "13:45:30.123", "0:0", "1:5", "1:", ":45", "1:2:3:4", "25:30", "100:00", "13:45 AM", "0:30 AM")
# text lookalikes
add("abc", "12abc", "abc12", "1 2", "1_000", "0x1F", "1e", "Infinity", "-Infinity", "NaN", "INF", "1/2abc", "N/A", "n/a", "-", "+", ".", ",", "$", "=1+1", "'5", "'abc", "@x", "5 apples", "00", "000", "0000.5")
# spaces
add("' 5 '", "  5", "5  ", "\t5", "5\t")
# quoted forms (the quotes are part of the file)
add('"5"', '"007"', '"TRUE"', '"1/2/2020"', '"abc"', '"a""b"', '""', '" "', '"1,234"', '"=1+1"')

values2 = []
values2 += ["1:60", "1:61", "1:99", "1:00:60", "1:00:61", "1:00:99", "9999:59", "10000:00", "1:2:3", "0:0:0", "1:45:30 PM", "1:45:30.5 PM", "12:30:15 AM", "13:00 PM", "0:00 PM", "12:60 PM", "1:45 a", "1:45 am", "1:45 A.M.", "1:45  PM", "1:45\u00a0PM", "01:45:30.12345", "1:45:30.", "1:00:00.0"]
values2 += ["1,2345,678", "12,3456", "1234,567", "1,234,5678", "12,345,678", "0,000", "00,123", "1,000.5", "1,000.", "1,234.5.6", "1,0000", "999,9999", "1,234E3", "1,234%", "1,234.567", "123,456,789", "1,000,000.00", "1.000,5", "1,00", "1,0000.5", "-1,2345", "$1,2345"]
values2 += ["---5", "-+5", "++5", "- -5", "-5-", "(--5)", "$--5", "-$-5", "$(5)", "-(5)", "(5)%", "(5.5)", "($5.5)", "-5%", "+5%", "- 5%", "5 -", "$5$", "5 $", "USD5", "$5.", "$.5", "$,5", "$ -5", "$1.5E3", "$5%", "-$5.25", "$0"]
values2 += ["1E307", "9.99999999999999E307", "1E308", "2.2250738585072014E-308", "1E-307", "1E-308", "1E+400", "1E5000", "1E00", "1.E3", ".E3", "1E+", "1 E3", "1E 3", "1d3", "123456789012345678", "1234567890123456.5", "0.000000000000000123456789012345678", "12345678901234.5678", "00000000000000000001", "-1234567890123456", "9007199254740993", "0.1234567890123456789"]
values2 += ["#N/A ", " #N/A", "#N/A!", "#REF", "#VALUE", "#DIV/0", "#NUM", "#NAME", "#NULL", "#GETTING_DATA!", "#UNKNOWN!", "#FIELD!", "#BLOCKED!", "#CONNECT!", "#EXTERNAL!", "#PYTHON!", "#BUSY!", "#SPILL", "#Calc!"]
values2 += ["Jan 15", "Jan 32", "Feb 30", "Feb 29", "Dec 31", "31-Dec", "31/12", "12/31", "13/1", "2-Jan-1899", "Jan-1900", "12/31/9999 23:59", "1/1/1900 0:00", "2020-1-2 1:2", "2020-01-02 1:45:30 PM", "2020/1/2 13:45", "1/2/2020 13:45:30.5", "Jan 2, 2020 3:00 PM", "2 Jan 2020 3:00", "20-Jan-20", "Jan 5, 20", "January 5, 1930", "Jan 5, 29", "Jan 5, 30", "1/2/29", "1/2/30", "1/2/00", "1/2/1", "1/2/100", "1/2/12345", "Sept 2020", "Sept-2020", "sep 2, 2020", "SEP 2, 2020", "Septe 2, 2020", "Janu 2, 2020", "Ja 2, 2020", "2 Sept 2020", "Mon, Jan 2, 2020", "2020-Jan-02", "02/Jan/2020", "2.Jan.2020", "1/2/2020 ", " 1/2/2020", "1 /2/2020", "1/ 2/2020", "1/2/ 2020", "1-2/2020", "1/2-2020", "2020-01", "2020-1", "2020", "1/1/1904", "2020-01-02 1", "2020-01-02  13:45", "Jan  2, 2020", "Jan 2,2020", "Jan 2 , 2020", "2-Jan-2020 13:45", "may 5, 2020", "MAY 5 2020", "Mar 5, 2020", "March 5, 2020", "Marc 5, 2020", "5 May 2020", "5-may-2020", "5-MAY-20", "5/May/2020"]
values2 += ["\u00a012", "12\u00a0", "1\u00a0234", "\u20125", "\u22125", "\uff15", "5\u00a0%", "١٢٣", "1\u066b5"]

with open(os.path.join(out, "values2.csv"), "w", encoding="utf-8-sig", newline="") as f:
    n = 0
    for raw in values2:
        if "," in raw and not raw.startswith('"'):
            raw = '"' + raw.replace('"', '""') + '"'
        n += 1
        f.write(f"{n},{raw}\r\n")

with open(os.path.join(out, "values.csv"), "w", encoding="utf-8-sig", newline="") as f:
    n = 0
    for raw in values:
        if raw.startswith("' ") and raw.endswith(" '"):
            raw = raw[1:-1]  # spaces kept around the number
        # A raw field containing the delimiter must be quoted to stay one field; the grid writes it quoted unless already quoted.
        if "," in raw and not raw.startswith('"'):
            raw = '"' + raw.replace('"', '""') + '"'
        n += 1
        f.write(f"{n},{raw}\r\n")

shapes = {
    "struct-quotes.csv": b'a,b,c\r\n1,"x,y","he said ""hi"""\r\n"line1\r\nline2",,\r\n,"",x\r\n"",,\r\nunq"uote,5" pipe,"q"x\r\n"a""",b,c\r\n',
    "struct-lf.csv": b"a,b\nc,d\n\ne,f\n",
    "struct-cr.csv": b"a,b\rc,d\re,f\r",
    "struct-ragged.csv": b"a,b,c\n1,2\n1,2,3,4\n5\n,\n",
    "struct-trailing.csv": b"a,b,\n1,2,\n,,\n",
    "struct-nolasteol.csv": b"a,b\n1,2",
    "struct-bom-utf8.csv": b"\xef\xbb\xbfname,city\r\nJos\xc3\xa9,Z\xc3\xbcrich\r\n\xe2\x82\xac5,\xe6\x97\xa5\xe6\x9c\xac\r\n",
    "struct-utf8-nobom.csv": b"name,city\r\nJos\xc3\xa9,Z\xc3\xbcrich\r\n\xe2\x82\xac5,\xe6\x97\xa5\xe6\x9c\xac\r\n",
    "struct-cp1252.csv": b"name,city\r\nJos\xe9,Z\xfcrich\r\n\x805,caf\xe9\r\n",
    "struct-utf16le.csv": "\ufeffname,city\r\nJosé,Zürich\r\n".encode("utf-16-le"),
    "struct-utf16le-tab.csv": "\ufeffname\tcity\r\nJosé\tZürich\r\n".encode("utf-16-le"),
    "struct-wide.csv": (",".join(f"c{i}" for i in range(300)) + "\r\n" + ",".join(str(i) for i in range(300)) + "\r\n").encode(),
    "struct-longtext.csv": ("a," + '"' + "x" * 40000 + '"' + "\r\n").encode(),
    "struct-sep-hint.csv": b"sep=;\r\na;b\r\n1;2\r\n",
}
for name, data in shapes.items():
    with open(os.path.join(out, name), "wb") as f:
        f.write(data)
print(f"{len(values) + len(values2)} value cases, {len(shapes)} shape files in {out}")
