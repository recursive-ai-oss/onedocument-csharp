// The record type numbers of [MS-XLSB] section 2.3, one constant per record name.
namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>The record type numbers of the binary workbook format ([MS-XLSB] §2.3), named after the records without the <c>Brt</c> prefix.</summary>
public static class XlsbRecordType
{
    /// <summary><c>BrtRowHdr</c>.</summary>
    public const int RowHdr = 0;
    /// <summary><c>BrtCellBlank</c>.</summary>
    public const int CellBlank = 1;
    /// <summary><c>BrtCellRk</c>.</summary>
    public const int CellRk = 2;
    /// <summary><c>BrtCellError</c>.</summary>
    public const int CellError = 3;
    /// <summary><c>BrtCellBool</c>.</summary>
    public const int CellBool = 4;
    /// <summary><c>BrtCellReal</c>.</summary>
    public const int CellReal = 5;
    /// <summary><c>BrtCellSt</c>.</summary>
    public const int CellSt = 6;
    /// <summary><c>BrtCellIsst</c>.</summary>
    public const int CellIsst = 7;
    /// <summary><c>BrtFmlaString</c>.</summary>
    public const int FmlaString = 8;
    /// <summary><c>BrtFmlaNum</c>.</summary>
    public const int FmlaNum = 9;
    /// <summary><c>BrtFmlaBool</c>.</summary>
    public const int FmlaBool = 10;
    /// <summary><c>BrtFmlaError</c>.</summary>
    public const int FmlaError = 11;
    /// <summary><c>BrtSSTItem</c>.</summary>
    public const int SSTItem = 19;
    /// <summary><c>BrtPCDIMissing</c>.</summary>
    public const int PCDIMissing = 20;
    /// <summary><c>BrtPCDINumber</c>.</summary>
    public const int PCDINumber = 21;
    /// <summary><c>BrtPCDIBoolean</c>.</summary>
    public const int PCDIBoolean = 22;
    /// <summary><c>BrtPCDIError</c>.</summary>
    public const int PCDIError = 23;
    /// <summary><c>BrtPCDIString</c>.</summary>
    public const int PCDIString = 24;
    /// <summary><c>BrtPCDIDatetime</c>.</summary>
    public const int PCDIDatetime = 25;
    /// <summary><c>BrtPCDIIndex</c>.</summary>
    public const int PCDIIndex = 26;
    /// <summary><c>BrtPCDIAMissing</c>.</summary>
    public const int PCDIAMissing = 27;
    /// <summary><c>BrtPCDIANumber</c>.</summary>
    public const int PCDIANumber = 28;
    /// <summary><c>BrtPCDIABoolean</c>.</summary>
    public const int PCDIABoolean = 29;
    /// <summary><c>BrtPCDIAError</c>.</summary>
    public const int PCDIAError = 30;
    /// <summary><c>BrtPCDIAString</c>.</summary>
    public const int PCDIAString = 31;
    /// <summary><c>BrtPCDIADatetime</c>.</summary>
    public const int PCDIADatetime = 32;
    /// <summary><c>BrtPCRRecord</c>.</summary>
    public const int PCRRecord = 33;
    /// <summary><c>BrtPCRRecordDt</c>.</summary>
    public const int PCRRecordDt = 34;
    /// <summary><c>BrtFRTBegin</c>.</summary>
    public const int FRTBegin = 35;
    /// <summary><c>BrtFRTEnd</c>.</summary>
    public const int FRTEnd = 36;
    /// <summary><c>BrtACBegin</c>.</summary>
    public const int ACBegin = 37;
    /// <summary><c>BrtACEnd</c>.</summary>
    public const int ACEnd = 38;
    /// <summary><c>BrtName</c>.</summary>
    public const int Name = 39;
    /// <summary><c>BrtIndexRowBlock</c>.</summary>
    public const int IndexRowBlock = 40;
    /// <summary><c>BrtIndexBlock</c>.</summary>
    public const int IndexBlock = 42;
    /// <summary><c>BrtFont</c>.</summary>
    public const int Font = 43;
    /// <summary><c>BrtFmt</c>.</summary>
    public const int Fmt = 44;
    /// <summary><c>BrtFill</c>.</summary>
    public const int Fill = 45;
    /// <summary><c>BrtBorder</c>.</summary>
    public const int Border = 46;
    /// <summary><c>BrtXF</c>.</summary>
    public const int XF = 47;
    /// <summary><c>BrtStyle</c>.</summary>
    public const int Style = 48;
    /// <summary><c>BrtCellMeta</c>.</summary>
    public const int CellMeta = 49;
    /// <summary><c>BrtValueMeta</c>.</summary>
    public const int ValueMeta = 50;
    /// <summary><c>BrtMdb</c>.</summary>
    public const int Mdb = 51;
    /// <summary><c>BrtBeginFmd</c>.</summary>
    public const int BeginFmd = 52;
    /// <summary><c>BrtEndFmd</c>.</summary>
    public const int EndFmd = 53;
    /// <summary><c>BrtBeginMdx</c>.</summary>
    public const int BeginMdx = 54;
    /// <summary><c>BrtEndMdx</c>.</summary>
    public const int EndMdx = 55;
    /// <summary><c>BrtBeginMdxTuple</c>.</summary>
    public const int BeginMdxTuple = 56;
    /// <summary><c>BrtEndMdxTuple</c>.</summary>
    public const int EndMdxTuple = 57;
    /// <summary><c>BrtMdxMbrIstr</c>.</summary>
    public const int MdxMbrIstr = 58;
    /// <summary><c>BrtStr</c>.</summary>
    public const int Str = 59;
    /// <summary><c>BrtColInfo</c>.</summary>
    public const int ColInfo = 60;
    /// <summary><c>BrtCellRString</c>.</summary>
    public const int CellRString = 62;
    /// <summary><c>BrtDVal</c>.</summary>
    public const int DVal = 64;
    /// <summary><c>BrtSxvcellNum</c>.</summary>
    public const int SxvcellNum = 65;
    /// <summary><c>BrtSxvcellStr</c>.</summary>
    public const int SxvcellStr = 66;
    /// <summary><c>BrtSxvcellBool</c>.</summary>
    public const int SxvcellBool = 67;
    /// <summary><c>BrtSxvcellErr</c>.</summary>
    public const int SxvcellErr = 68;
    /// <summary><c>BrtSxvcellDate</c>.</summary>
    public const int SxvcellDate = 69;
    /// <summary><c>BrtSxvcellNil</c>.</summary>
    public const int SxvcellNil = 70;
    /// <summary><c>BrtFileVersion</c>.</summary>
    public const int FileVersion = 128;
    /// <summary><c>BrtBeginSheet</c>.</summary>
    public const int BeginSheet = 129;
    /// <summary><c>BrtEndSheet</c>.</summary>
    public const int EndSheet = 130;
    /// <summary><c>BrtBeginBook</c>.</summary>
    public const int BeginBook = 131;
    /// <summary><c>BrtEndBook</c>.</summary>
    public const int EndBook = 132;
    /// <summary><c>BrtBeginWsViews</c>.</summary>
    public const int BeginWsViews = 133;
    /// <summary><c>BrtEndWsViews</c>.</summary>
    public const int EndWsViews = 134;
    /// <summary><c>BrtBeginBookViews</c>.</summary>
    public const int BeginBookViews = 135;
    /// <summary><c>BrtEndBookViews</c>.</summary>
    public const int EndBookViews = 136;
    /// <summary><c>BrtBeginWsView</c>.</summary>
    public const int BeginWsView = 137;
    /// <summary><c>BrtEndWsView</c>.</summary>
    public const int EndWsView = 138;
    /// <summary><c>BrtBeginCsViews</c>.</summary>
    public const int BeginCsViews = 139;
    /// <summary><c>BrtEndCsViews</c>.</summary>
    public const int EndCsViews = 140;
    /// <summary><c>BrtBeginCsView</c>.</summary>
    public const int BeginCsView = 141;
    /// <summary><c>BrtEndCsView</c>.</summary>
    public const int EndCsView = 142;
    /// <summary><c>BrtBeginBundleShs</c>.</summary>
    public const int BeginBundleShs = 143;
    /// <summary><c>BrtEndBundleShs</c>.</summary>
    public const int EndBundleShs = 144;
    /// <summary><c>BrtBeginSheetData</c>.</summary>
    public const int BeginSheetData = 145;
    /// <summary><c>BrtEndSheetData</c>.</summary>
    public const int EndSheetData = 146;
    /// <summary><c>BrtWsProp</c>.</summary>
    public const int WsProp = 147;
    /// <summary><c>BrtWsDim</c>.</summary>
    public const int WsDim = 148;
    /// <summary><c>BrtPane</c>.</summary>
    public const int Pane = 151;
    /// <summary><c>BrtSel</c>.</summary>
    public const int Sel = 152;
    /// <summary><c>BrtWbProp</c>.</summary>
    public const int WbProp = 153;
    /// <summary><c>BrtWbFactoid</c>.</summary>
    public const int WbFactoid = 154;
    /// <summary><c>BrtFileRecover</c>.</summary>
    public const int FileRecover = 155;
    /// <summary><c>BrtBundleSh</c>.</summary>
    public const int BundleSh = 156;
    /// <summary><c>BrtCalcProp</c>.</summary>
    public const int CalcProp = 157;
    /// <summary><c>BrtBookView</c>.</summary>
    public const int BookView = 158;
    /// <summary><c>BrtBeginSst</c>.</summary>
    public const int BeginSst = 159;
    /// <summary><c>BrtEndSst</c>.</summary>
    public const int EndSst = 160;
    /// <summary><c>BrtBeginAFilter</c>.</summary>
    public const int BeginAFilter = 161;
    /// <summary><c>BrtEndAFilter</c>.</summary>
    public const int EndAFilter = 162;
    /// <summary><c>BrtBeginFilterColumn</c>.</summary>
    public const int BeginFilterColumn = 163;
    /// <summary><c>BrtEndFilterColumn</c>.</summary>
    public const int EndFilterColumn = 164;
    /// <summary><c>BrtBeginFilters</c>.</summary>
    public const int BeginFilters = 165;
    /// <summary><c>BrtEndFilters</c>.</summary>
    public const int EndFilters = 166;
    /// <summary><c>BrtFilter</c>.</summary>
    public const int Filter = 167;
    /// <summary><c>BrtColorFilter</c>.</summary>
    public const int ColorFilter = 168;
    /// <summary><c>BrtIconFilter</c>.</summary>
    public const int IconFilter = 169;
    /// <summary><c>BrtTop10Filter</c>.</summary>
    public const int Top10Filter = 170;
    /// <summary><c>BrtDynamicFilter</c>.</summary>
    public const int DynamicFilter = 171;
    /// <summary><c>BrtBeginCustomFilters</c>.</summary>
    public const int BeginCustomFilters = 172;
    /// <summary><c>BrtEndCustomFilters</c>.</summary>
    public const int EndCustomFilters = 173;
    /// <summary><c>BrtCustomFilter</c>.</summary>
    public const int CustomFilter = 174;
    /// <summary><c>BrtAFilterDateGroupItem</c>.</summary>
    public const int AFilterDateGroupItem = 175;
    /// <summary><c>BrtMergeCell</c>.</summary>
    public const int MergeCell = 176;
    /// <summary><c>BrtBeginMergeCells</c>.</summary>
    public const int BeginMergeCells = 177;
    /// <summary><c>BrtEndMergeCells</c>.</summary>
    public const int EndMergeCells = 178;
    /// <summary><c>BrtBeginPivotCacheDef</c>.</summary>
    public const int BeginPivotCacheDef = 179;
    /// <summary><c>BrtEndPivotCacheDef</c>.</summary>
    public const int EndPivotCacheDef = 180;
    /// <summary><c>BrtBeginPCDFields</c>.</summary>
    public const int BeginPCDFields = 181;
    /// <summary><c>BrtEndPCDFields</c>.</summary>
    public const int EndPCDFields = 182;
    /// <summary><c>BrtBeginPCDField</c>.</summary>
    public const int BeginPCDField = 183;
    /// <summary><c>BrtEndPCDField</c>.</summary>
    public const int EndPCDField = 184;
    /// <summary><c>BrtBeginPCDSource</c>.</summary>
    public const int BeginPCDSource = 185;
    /// <summary><c>BrtEndPCDSource</c>.</summary>
    public const int EndPCDSource = 186;
    /// <summary><c>BrtBeginPCDSRange</c>.</summary>
    public const int BeginPCDSRange = 187;
    /// <summary><c>BrtEndPCDSRange</c>.</summary>
    public const int EndPCDSRange = 188;
    /// <summary><c>BrtBeginPCDFAtbl</c>.</summary>
    public const int BeginPCDFAtbl = 189;
    /// <summary><c>BrtEndPCDFAtbl</c>.</summary>
    public const int EndPCDFAtbl = 190;
    /// <summary><c>BrtBeginPCDIRun</c>.</summary>
    public const int BeginPCDIRun = 191;
    /// <summary><c>BrtEndPCDIRun</c>.</summary>
    public const int EndPCDIRun = 192;
    /// <summary><c>BrtBeginPivotCacheRecords</c>.</summary>
    public const int BeginPivotCacheRecords = 193;
    /// <summary><c>BrtEndPivotCacheRecords</c>.</summary>
    public const int EndPivotCacheRecords = 194;
    /// <summary><c>BrtBeginPCDHierarchies</c>.</summary>
    public const int BeginPCDHierarchies = 195;
    /// <summary><c>BrtEndPCDHierarchies</c>.</summary>
    public const int EndPCDHierarchies = 196;
    /// <summary><c>BrtBeginPCDHierarchy</c>.</summary>
    public const int BeginPCDHierarchy = 197;
    /// <summary><c>BrtEndPCDHierarchy</c>.</summary>
    public const int EndPCDHierarchy = 198;
    /// <summary><c>BrtBeginPCDHFieldsUsage</c>.</summary>
    public const int BeginPCDHFieldsUsage = 199;
    /// <summary><c>BrtEndPCDHFieldsUsage</c>.</summary>
    public const int EndPCDHFieldsUsage = 200;
    /// <summary><c>BrtBeginExtConnection</c>.</summary>
    public const int BeginExtConnection = 201;
    /// <summary><c>BrtEndExtConnection</c>.</summary>
    public const int EndExtConnection = 202;
    /// <summary><c>BrtBeginECDbProps</c>.</summary>
    public const int BeginECDbProps = 203;
    /// <summary><c>BrtEndECDbProps</c>.</summary>
    public const int EndECDbProps = 204;
    /// <summary><c>BrtBeginECOlapProps</c>.</summary>
    public const int BeginECOlapProps = 205;
    /// <summary><c>BrtEndECOlapProps</c>.</summary>
    public const int EndECOlapProps = 206;
    /// <summary><c>BrtBeginPCDSConsol</c>.</summary>
    public const int BeginPCDSConsol = 207;
    /// <summary><c>BrtEndPCDSConsol</c>.</summary>
    public const int EndPCDSConsol = 208;
    /// <summary><c>BrtBeginPCDSCPages</c>.</summary>
    public const int BeginPCDSCPages = 209;
    /// <summary><c>BrtEndPCDSCPages</c>.</summary>
    public const int EndPCDSCPages = 210;
    /// <summary><c>BrtBeginPCDSCPage</c>.</summary>
    public const int BeginPCDSCPage = 211;
    /// <summary><c>BrtEndPCDSCPage</c>.</summary>
    public const int EndPCDSCPage = 212;
    /// <summary><c>BrtBeginPCDSCPItem</c>.</summary>
    public const int BeginPCDSCPItem = 213;
    /// <summary><c>BrtEndPCDSCPItem</c>.</summary>
    public const int EndPCDSCPItem = 214;
    /// <summary><c>BrtBeginPCDSCSets</c>.</summary>
    public const int BeginPCDSCSets = 215;
    /// <summary><c>BrtEndPCDSCSets</c>.</summary>
    public const int EndPCDSCSets = 216;
    /// <summary><c>BrtBeginPCDSCSet</c>.</summary>
    public const int BeginPCDSCSet = 217;
    /// <summary><c>BrtEndPCDSCSet</c>.</summary>
    public const int EndPCDSCSet = 218;
    /// <summary><c>BrtBeginPCDFGroup</c>.</summary>
    public const int BeginPCDFGroup = 219;
    /// <summary><c>BrtEndPCDFGroup</c>.</summary>
    public const int EndPCDFGroup = 220;
    /// <summary><c>BrtBeginPCDFGItems</c>.</summary>
    public const int BeginPCDFGItems = 221;
    /// <summary><c>BrtEndPCDFGItems</c>.</summary>
    public const int EndPCDFGItems = 222;
    /// <summary><c>BrtBeginPCDFGRange</c>.</summary>
    public const int BeginPCDFGRange = 223;
    /// <summary><c>BrtEndPCDFGRange</c>.</summary>
    public const int EndPCDFGRange = 224;
    /// <summary><c>BrtBeginPCDFGDiscrete</c>.</summary>
    public const int BeginPCDFGDiscrete = 225;
    /// <summary><c>BrtEndPCDFGDiscrete</c>.</summary>
    public const int EndPCDFGDiscrete = 226;
    /// <summary><c>BrtBeginPCDSDTupleCache</c>.</summary>
    public const int BeginPCDSDTupleCache = 227;
    /// <summary><c>BrtEndPCDSDTupleCache</c>.</summary>
    public const int EndPCDSDTupleCache = 228;
    /// <summary><c>BrtBeginPCDSDTCEntries</c>.</summary>
    public const int BeginPCDSDTCEntries = 229;
    /// <summary><c>BrtEndPCDSDTCEntries</c>.</summary>
    public const int EndPCDSDTCEntries = 230;
    /// <summary><c>BrtBeginPCDSDTCEMembers</c>.</summary>
    public const int BeginPCDSDTCEMembers = 231;
    /// <summary><c>BrtEndPCDSDTCEMembers</c>.</summary>
    public const int EndPCDSDTCEMembers = 232;
    /// <summary><c>BrtBeginPCDSDTCEMember</c>.</summary>
    public const int BeginPCDSDTCEMember = 233;
    /// <summary><c>BrtEndPCDSDTCEMember</c>.</summary>
    public const int EndPCDSDTCEMember = 234;
    /// <summary><c>BrtBeginPCDSDTCQueries</c>.</summary>
    public const int BeginPCDSDTCQueries = 235;
    /// <summary><c>BrtEndPCDSDTCQueries</c>.</summary>
    public const int EndPCDSDTCQueries = 236;
    /// <summary><c>BrtBeginPCDSDTCQuery</c>.</summary>
    public const int BeginPCDSDTCQuery = 237;
    /// <summary><c>BrtEndPCDSDTCQuery</c>.</summary>
    public const int EndPCDSDTCQuery = 238;
    /// <summary><c>BrtBeginPCDSDTCSets</c>.</summary>
    public const int BeginPCDSDTCSets = 239;
    /// <summary><c>BrtEndPCDSDTCSets</c>.</summary>
    public const int EndPCDSDTCSets = 240;
    /// <summary><c>BrtBeginPCDSDTCSet</c>.</summary>
    public const int BeginPCDSDTCSet = 241;
    /// <summary><c>BrtEndPCDSDTCSet</c>.</summary>
    public const int EndPCDSDTCSet = 242;
    /// <summary><c>BrtBeginPCDCalcItems</c>.</summary>
    public const int BeginPCDCalcItems = 243;
    /// <summary><c>BrtEndPCDCalcItems</c>.</summary>
    public const int EndPCDCalcItems = 244;
    /// <summary><c>BrtBeginPCDCalcItem</c>.</summary>
    public const int BeginPCDCalcItem = 245;
    /// <summary><c>BrtEndPCDCalcItem</c>.</summary>
    public const int EndPCDCalcItem = 246;
    /// <summary><c>BrtBeginPRule</c>.</summary>
    public const int BeginPRule = 247;
    /// <summary><c>BrtEndPRule</c>.</summary>
    public const int EndPRule = 248;
    /// <summary><c>BrtBeginPRFilters</c>.</summary>
    public const int BeginPRFilters = 249;
    /// <summary><c>BrtEndPRFilters</c>.</summary>
    public const int EndPRFilters = 250;
    /// <summary><c>BrtBeginPRFilter</c>.</summary>
    public const int BeginPRFilter = 251;
    /// <summary><c>BrtEndPRFilter</c>.</summary>
    public const int EndPRFilter = 252;
    /// <summary><c>BrtBeginPNames</c>.</summary>
    public const int BeginPNames = 253;
    /// <summary><c>BrtEndPNames</c>.</summary>
    public const int EndPNames = 254;
    /// <summary><c>BrtBeginPName</c>.</summary>
    public const int BeginPName = 255;
    /// <summary><c>BrtEndPName</c>.</summary>
    public const int EndPName = 256;
    /// <summary><c>BrtBeginPNPairs</c>.</summary>
    public const int BeginPNPairs = 257;
    /// <summary><c>BrtEndPNPairs</c>.</summary>
    public const int EndPNPairs = 258;
    /// <summary><c>BrtBeginPNPair</c>.</summary>
    public const int BeginPNPair = 259;
    /// <summary><c>BrtEndPNPair</c>.</summary>
    public const int EndPNPair = 260;
    /// <summary><c>BrtBeginECWebProps</c>.</summary>
    public const int BeginECWebProps = 261;
    /// <summary><c>BrtEndECWebProps</c>.</summary>
    public const int EndECWebProps = 262;
    /// <summary><c>BrtBeginEcWpTables</c>.</summary>
    public const int BeginEcWpTables = 263;
    /// <summary><c>BrtEndECWPTables</c>.</summary>
    public const int EndECWPTables = 264;
    /// <summary><c>BrtBeginECParams</c>.</summary>
    public const int BeginECParams = 265;
    /// <summary><c>BrtEndECParams</c>.</summary>
    public const int EndECParams = 266;
    /// <summary><c>BrtBeginECParam</c>.</summary>
    public const int BeginECParam = 267;
    /// <summary><c>BrtEndECParam</c>.</summary>
    public const int EndECParam = 268;
    /// <summary><c>BrtBeginPCDKPIs</c>.</summary>
    public const int BeginPCDKPIs = 269;
    /// <summary><c>BrtEndPCDKPIs</c>.</summary>
    public const int EndPCDKPIs = 270;
    /// <summary><c>BrtBeginPCDKPI</c>.</summary>
    public const int BeginPCDKPI = 271;
    /// <summary><c>BrtEndPCDKPI</c>.</summary>
    public const int EndPCDKPI = 272;
    /// <summary><c>BrtBeginDims</c>.</summary>
    public const int BeginDims = 273;
    /// <summary><c>BrtEndDims</c>.</summary>
    public const int EndDims = 274;
    /// <summary><c>BrtBeginDim</c>.</summary>
    public const int BeginDim = 275;
    /// <summary><c>BrtEndDim</c>.</summary>
    public const int EndDim = 276;
    /// <summary><c>BrtIndexPartEnd</c>.</summary>
    public const int IndexPartEnd = 277;
    /// <summary><c>BrtBeginStyleSheet</c>.</summary>
    public const int BeginStyleSheet = 278;
    /// <summary><c>BrtEndStyleSheet</c>.</summary>
    public const int EndStyleSheet = 279;
    /// <summary><c>BrtBeginSXView</c>.</summary>
    public const int BeginSXView = 280;
    /// <summary><c>BrtEndSXVI</c>.</summary>
    public const int EndSXVI = 281;
    /// <summary><c>BrtBeginSXVI</c>.</summary>
    public const int BeginSXVI = 282;
    /// <summary><c>BrtBeginSXVIs</c>.</summary>
    public const int BeginSXVIs = 283;
    /// <summary><c>BrtEndSXVIs</c>.</summary>
    public const int EndSXVIs = 284;
    /// <summary><c>BrtBeginSXVD</c>.</summary>
    public const int BeginSXVD = 285;
    /// <summary><c>BrtEndSXVD</c>.</summary>
    public const int EndSXVD = 286;
    /// <summary><c>BrtBeginSXVDs</c>.</summary>
    public const int BeginSXVDs = 287;
    /// <summary><c>BrtEndSXVDs</c>.</summary>
    public const int EndSXVDs = 288;
    /// <summary><c>BrtBeginSXPI</c>.</summary>
    public const int BeginSXPI = 289;
    /// <summary><c>BrtEndSXPI</c>.</summary>
    public const int EndSXPI = 290;
    /// <summary><c>BrtBeginSXPIs</c>.</summary>
    public const int BeginSXPIs = 291;
    /// <summary><c>BrtEndSXPIs</c>.</summary>
    public const int EndSXPIs = 292;
    /// <summary><c>BrtBeginSXDI</c>.</summary>
    public const int BeginSXDI = 293;
    /// <summary><c>BrtEndSXDI</c>.</summary>
    public const int EndSXDI = 294;
    /// <summary><c>BrtBeginSXDIs</c>.</summary>
    public const int BeginSXDIs = 295;
    /// <summary><c>BrtEndSXDIs</c>.</summary>
    public const int EndSXDIs = 296;
    /// <summary><c>BrtBeginSXLI</c>.</summary>
    public const int BeginSXLI = 297;
    /// <summary><c>BrtEndSXLI</c>.</summary>
    public const int EndSXLI = 298;
    /// <summary><c>BrtBeginSXLIRws</c>.</summary>
    public const int BeginSXLIRws = 299;
    /// <summary><c>BrtEndSXLIRws</c>.</summary>
    public const int EndSXLIRws = 300;
    /// <summary><c>BrtBeginSXLICols</c>.</summary>
    public const int BeginSXLICols = 301;
    /// <summary><c>BrtEndSXLICols</c>.</summary>
    public const int EndSXLICols = 302;
    /// <summary><c>BrtBeginSXFormat</c>.</summary>
    public const int BeginSXFormat = 303;
    /// <summary><c>BrtEndSXFormat</c>.</summary>
    public const int EndSXFormat = 304;
    /// <summary><c>BrtBeginSXFormats</c>.</summary>
    public const int BeginSXFormats = 305;
    /// <summary><c>BrtEndSxFormats</c>.</summary>
    public const int EndSxFormats = 306;
    /// <summary><c>BrtBeginSxSelect</c>.</summary>
    public const int BeginSxSelect = 307;
    /// <summary><c>BrtEndSxSelect</c>.</summary>
    public const int EndSxSelect = 308;
    /// <summary><c>BrtBeginISXVDRws</c>.</summary>
    public const int BeginISXVDRws = 309;
    /// <summary><c>BrtEndISXVDRws</c>.</summary>
    public const int EndISXVDRws = 310;
    /// <summary><c>BrtBeginISXVDCols</c>.</summary>
    public const int BeginISXVDCols = 311;
    /// <summary><c>BrtEndISXVDCols</c>.</summary>
    public const int EndISXVDCols = 312;
    /// <summary><c>BrtEndSXLocation</c>.</summary>
    public const int EndSXLocation = 313;
    /// <summary><c>BrtBeginSXLocation</c>.</summary>
    public const int BeginSXLocation = 314;
    /// <summary><c>BrtEndSXView</c>.</summary>
    public const int EndSXView = 315;
    /// <summary><c>BrtBeginSXTHs</c>.</summary>
    public const int BeginSXTHs = 316;
    /// <summary><c>BrtEndSXTHs</c>.</summary>
    public const int EndSXTHs = 317;
    /// <summary><c>BrtBeginSXTH</c>.</summary>
    public const int BeginSXTH = 318;
    /// <summary><c>BrtEndSXTH</c>.</summary>
    public const int EndSXTH = 319;
    /// <summary><c>BrtBeginISXTHRws</c>.</summary>
    public const int BeginISXTHRws = 320;
    /// <summary><c>BrtEndISXTHRws</c>.</summary>
    public const int EndISXTHRws = 321;
    /// <summary><c>BrtBeginISXTHCols</c>.</summary>
    public const int BeginISXTHCols = 322;
    /// <summary><c>BrtEndISXTHCols</c>.</summary>
    public const int EndISXTHCols = 323;
    /// <summary><c>BrtBeginSXTDMPS</c>.</summary>
    public const int BeginSXTDMPS = 324;
    /// <summary><c>BrtEndSXTDMPs</c>.</summary>
    public const int EndSXTDMPs = 325;
    /// <summary><c>BrtBeginSXTDMP</c>.</summary>
    public const int BeginSXTDMP = 326;
    /// <summary><c>BrtEndSXTDMP</c>.</summary>
    public const int EndSXTDMP = 327;
    /// <summary><c>BrtBeginSXTHItems</c>.</summary>
    public const int BeginSXTHItems = 328;
    /// <summary><c>BrtEndSXTHItems</c>.</summary>
    public const int EndSXTHItems = 329;
    /// <summary><c>BrtBeginSXTHItem</c>.</summary>
    public const int BeginSXTHItem = 330;
    /// <summary><c>BrtEndSXTHItem</c>.</summary>
    public const int EndSXTHItem = 331;
    /// <summary><c>BrtBeginMetadata</c>.</summary>
    public const int BeginMetadata = 332;
    /// <summary><c>BrtEndMetadata</c>.</summary>
    public const int EndMetadata = 333;
    /// <summary><c>BrtBeginEsmdtinfo</c>.</summary>
    public const int BeginEsmdtinfo = 334;
    /// <summary><c>BrtMdtinfo</c>.</summary>
    public const int Mdtinfo = 335;
    /// <summary><c>BrtEndEsmdtinfo</c>.</summary>
    public const int EndEsmdtinfo = 336;
    /// <summary><c>BrtBeginEsmdb</c>.</summary>
    public const int BeginEsmdb = 337;
    /// <summary><c>BrtEndEsmdb</c>.</summary>
    public const int EndEsmdb = 338;
    /// <summary><c>BrtBeginEsfmd</c>.</summary>
    public const int BeginEsfmd = 339;
    /// <summary><c>BrtEndEsfmd</c>.</summary>
    public const int EndEsfmd = 340;
    /// <summary><c>BrtBeginSingleCells</c>.</summary>
    public const int BeginSingleCells = 341;
    /// <summary><c>BrtEndSingleCells</c>.</summary>
    public const int EndSingleCells = 342;
    /// <summary><c>BrtBeginList</c>.</summary>
    public const int BeginList = 343;
    /// <summary><c>BrtEndList</c>.</summary>
    public const int EndList = 344;
    /// <summary><c>BrtBeginListCols</c>.</summary>
    public const int BeginListCols = 345;
    /// <summary><c>BrtEndListCols</c>.</summary>
    public const int EndListCols = 346;
    /// <summary><c>BrtBeginListCol</c>.</summary>
    public const int BeginListCol = 347;
    /// <summary><c>BrtEndListCol</c>.</summary>
    public const int EndListCol = 348;
    /// <summary><c>BrtBeginListXmlCPr</c>.</summary>
    public const int BeginListXmlCPr = 349;
    /// <summary><c>BrtEndListXmlCPr</c>.</summary>
    public const int EndListXmlCPr = 350;
    /// <summary><c>BrtListCCFmla</c>.</summary>
    public const int ListCCFmla = 351;
    /// <summary><c>BrtListTrFmla</c>.</summary>
    public const int ListTrFmla = 352;
    /// <summary><c>BrtBeginExternals</c>.</summary>
    public const int BeginExternals = 353;
    /// <summary><c>BrtEndExternals</c>.</summary>
    public const int EndExternals = 354;
    /// <summary><c>BrtSupBookSrc</c>.</summary>
    public const int SupBookSrc = 355;
    /// <summary><c>BrtSupSelf</c>.</summary>
    public const int SupSelf = 357;
    /// <summary><c>BrtSupSame</c>.</summary>
    public const int SupSame = 358;
    /// <summary><c>BrtSupTabs</c>.</summary>
    public const int SupTabs = 359;
    /// <summary><c>BrtBeginSupBook</c>.</summary>
    public const int BeginSupBook = 360;
    /// <summary><c>BrtPlaceholderName</c>.</summary>
    public const int PlaceholderName = 361;
    /// <summary><c>BrtExternSheet</c>.</summary>
    public const int ExternSheet = 362;
    /// <summary><c>BrtExternTableStart</c>.</summary>
    public const int ExternTableStart = 363;
    /// <summary><c>BrtExternTableEnd</c>.</summary>
    public const int ExternTableEnd = 364;
    /// <summary><c>BrtExternRowHdr</c>.</summary>
    public const int ExternRowHdr = 366;
    /// <summary><c>BrtExternCellBlank</c>.</summary>
    public const int ExternCellBlank = 367;
    /// <summary><c>BrtExternCellReal</c>.</summary>
    public const int ExternCellReal = 368;
    /// <summary><c>BrtExternCellBool</c>.</summary>
    public const int ExternCellBool = 369;
    /// <summary><c>BrtExternCellError</c>.</summary>
    public const int ExternCellError = 370;
    /// <summary><c>BrtExternCellString</c>.</summary>
    public const int ExternCellString = 371;
    /// <summary><c>BrtBeginEsmdx</c>.</summary>
    public const int BeginEsmdx = 372;
    /// <summary><c>BrtEndEsmdx</c>.</summary>
    public const int EndEsmdx = 373;
    /// <summary><c>BrtBeginMdxSet</c>.</summary>
    public const int BeginMdxSet = 374;
    /// <summary><c>BrtEndMdxSet</c>.</summary>
    public const int EndMdxSet = 375;
    /// <summary><c>BrtBeginMdxMbrProp</c>.</summary>
    public const int BeginMdxMbrProp = 376;
    /// <summary><c>BrtEndMdxMbrProp</c>.</summary>
    public const int EndMdxMbrProp = 377;
    /// <summary><c>BrtBeginMdxKPI</c>.</summary>
    public const int BeginMdxKPI = 378;
    /// <summary><c>BrtEndMdxKPI</c>.</summary>
    public const int EndMdxKPI = 379;
    /// <summary><c>BrtBeginEsstr</c>.</summary>
    public const int BeginEsstr = 380;
    /// <summary><c>BrtEndEsstr</c>.</summary>
    public const int EndEsstr = 381;
    /// <summary><c>BrtBeginPRFItem</c>.</summary>
    public const int BeginPRFItem = 382;
    /// <summary><c>BrtEndPRFItem</c>.</summary>
    public const int EndPRFItem = 383;
    /// <summary><c>BrtBeginPivotCacheIDs</c>.</summary>
    public const int BeginPivotCacheIDs = 384;
    /// <summary><c>BrtEndPivotCacheIDs</c>.</summary>
    public const int EndPivotCacheIDs = 385;
    /// <summary><c>BrtBeginPivotCacheID</c>.</summary>
    public const int BeginPivotCacheID = 386;
    /// <summary><c>BrtEndPivotCacheID</c>.</summary>
    public const int EndPivotCacheID = 387;
    /// <summary><c>BrtBeginISXVIs</c>.</summary>
    public const int BeginISXVIs = 388;
    /// <summary><c>BrtEndISXVIs</c>.</summary>
    public const int EndISXVIs = 389;
    /// <summary><c>BrtBeginColInfos</c>.</summary>
    public const int BeginColInfos = 390;
    /// <summary><c>BrtEndColInfos</c>.</summary>
    public const int EndColInfos = 391;
    /// <summary><c>BrtBeginRwBrk</c>.</summary>
    public const int BeginRwBrk = 392;
    /// <summary><c>BrtEndRwBrk</c>.</summary>
    public const int EndRwBrk = 393;
    /// <summary><c>BrtBeginColBrk</c>.</summary>
    public const int BeginColBrk = 394;
    /// <summary><c>BrtEndColBrk</c>.</summary>
    public const int EndColBrk = 395;
    /// <summary><c>BrtBrk</c>.</summary>
    public const int Brk = 396;
    /// <summary><c>BrtUserBookView</c>.</summary>
    public const int UserBookView = 397;
    /// <summary><c>BrtInfo</c>.</summary>
    public const int Info = 398;
    /// <summary><c>BrtCUsr</c>.</summary>
    public const int CUsr = 399;
    /// <summary><c>BrtUsr</c>.</summary>
    public const int Usr = 400;
    /// <summary><c>BrtBeginUsers</c>.</summary>
    public const int BeginUsers = 401;
    /// <summary><c>BrtEOF</c>.</summary>
    public const int EOF = 403;
    /// <summary><c>BrtUCR</c>.</summary>
    public const int UCR = 404;
    /// <summary><c>BrtRRInsDel</c>.</summary>
    public const int RRInsDel = 405;
    /// <summary><c>BrtRREndInsDel</c>.</summary>
    public const int RREndInsDel = 406;
    /// <summary><c>BrtRRMove</c>.</summary>
    public const int RRMove = 407;
    /// <summary><c>BrtRREndMove</c>.</summary>
    public const int RREndMove = 408;
    /// <summary><c>BrtRRChgCell</c>.</summary>
    public const int RRChgCell = 409;
    /// <summary><c>BrtRREndChgCell</c>.</summary>
    public const int RREndChgCell = 410;
    /// <summary><c>BrtRRHeader</c>.</summary>
    public const int RRHeader = 411;
    /// <summary><c>BrtRRUserView</c>.</summary>
    public const int RRUserView = 412;
    /// <summary><c>BrtRRRenSheet</c>.</summary>
    public const int RRRenSheet = 413;
    /// <summary><c>BrtRRInsertSh</c>.</summary>
    public const int RRInsertSh = 414;
    /// <summary><c>BrtRRDefName</c>.</summary>
    public const int RRDefName = 415;
    /// <summary><c>BrtRRNote</c>.</summary>
    public const int RRNote = 416;
    /// <summary><c>BrtRRConflict</c>.</summary>
    public const int RRConflict = 417;
    /// <summary><c>BrtRRTQSIF</c>.</summary>
    public const int RRTQSIF = 418;
    /// <summary><c>BrtRRFormat</c>.</summary>
    public const int RRFormat = 419;
    /// <summary><c>BrtRREndFormat</c>.</summary>
    public const int RREndFormat = 420;
    /// <summary><c>BrtRRAutoFmt</c>.</summary>
    public const int RRAutoFmt = 421;
    /// <summary><c>BrtBeginUserShViews</c>.</summary>
    public const int BeginUserShViews = 422;
    /// <summary><c>BrtBeginUserShView</c>.</summary>
    public const int BeginUserShView = 423;
    /// <summary><c>BrtEndUserShView</c>.</summary>
    public const int EndUserShView = 424;
    /// <summary><c>BrtEndUserShViews</c>.</summary>
    public const int EndUserShViews = 425;
    /// <summary><c>BrtArrFmla</c>.</summary>
    public const int ArrFmla = 426;
    /// <summary><c>BrtShrFmla</c>.</summary>
    public const int ShrFmla = 427;
    /// <summary><c>BrtTable</c>.</summary>
    public const int Table = 428;
    /// <summary><c>BrtBeginExtConnections</c>.</summary>
    public const int BeginExtConnections = 429;
    /// <summary><c>BrtEndExtConnections</c>.</summary>
    public const int EndExtConnections = 430;
    /// <summary><c>BrtBeginPCDCalcMems</c>.</summary>
    public const int BeginPCDCalcMems = 431;
    /// <summary><c>BrtEndPCDCalcMems</c>.</summary>
    public const int EndPCDCalcMems = 432;
    /// <summary><c>BrtBeginPCDCalcMem</c>.</summary>
    public const int BeginPCDCalcMem = 433;
    /// <summary><c>BrtEndPCDCalcMem</c>.</summary>
    public const int EndPCDCalcMem = 434;
    /// <summary><c>BrtBeginPCDHGLevels</c>.</summary>
    public const int BeginPCDHGLevels = 435;
    /// <summary><c>BrtEndPCDHGLevels</c>.</summary>
    public const int EndPCDHGLevels = 436;
    /// <summary><c>BrtBeginPCDHGLevel</c>.</summary>
    public const int BeginPCDHGLevel = 437;
    /// <summary><c>BrtEndPCDHGLevel</c>.</summary>
    public const int EndPCDHGLevel = 438;
    /// <summary><c>BrtBeginPCDHGLGroups</c>.</summary>
    public const int BeginPCDHGLGroups = 439;
    /// <summary><c>BrtEndPCDHGLGroups</c>.</summary>
    public const int EndPCDHGLGroups = 440;
    /// <summary><c>BrtBeginPCDHGLGroup</c>.</summary>
    public const int BeginPCDHGLGroup = 441;
    /// <summary><c>BrtEndPCDHGLGroup</c>.</summary>
    public const int EndPCDHGLGroup = 442;
    /// <summary><c>BrtBeginPCDHGLGMembers</c>.</summary>
    public const int BeginPCDHGLGMembers = 443;
    /// <summary><c>BrtEndPCDHGLGMembers</c>.</summary>
    public const int EndPCDHGLGMembers = 444;
    /// <summary><c>BrtBeginPCDHGLGMember</c>.</summary>
    public const int BeginPCDHGLGMember = 445;
    /// <summary><c>BrtEndPCDHGLGMember</c>.</summary>
    public const int EndPCDHGLGMember = 446;
    /// <summary><c>BrtBeginQSI</c>.</summary>
    public const int BeginQSI = 447;
    /// <summary><c>BrtEndQSI</c>.</summary>
    public const int EndQSI = 448;
    /// <summary><c>BrtBeginQSIR</c>.</summary>
    public const int BeginQSIR = 449;
    /// <summary><c>BrtEndQSIR</c>.</summary>
    public const int EndQSIR = 450;
    /// <summary><c>BrtBeginDeletedNames</c>.</summary>
    public const int BeginDeletedNames = 451;
    /// <summary><c>BrtEndDeletedNames</c>.</summary>
    public const int EndDeletedNames = 452;
    /// <summary><c>BrtBeginDeletedName</c>.</summary>
    public const int BeginDeletedName = 453;
    /// <summary><c>BrtEndDeletedName</c>.</summary>
    public const int EndDeletedName = 454;
    /// <summary><c>BrtBeginQSIFs</c>.</summary>
    public const int BeginQSIFs = 455;
    /// <summary><c>BrtEndQSIFs</c>.</summary>
    public const int EndQSIFs = 456;
    /// <summary><c>BrtBeginQSIF</c>.</summary>
    public const int BeginQSIF = 457;
    /// <summary><c>BrtEndQSIF</c>.</summary>
    public const int EndQSIF = 458;
    /// <summary><c>BrtBeginAutoSortScope</c>.</summary>
    public const int BeginAutoSortScope = 459;
    /// <summary><c>BrtEndAutoSortScope</c>.</summary>
    public const int EndAutoSortScope = 460;
    /// <summary><c>BrtBeginConditionalFormatting</c>.</summary>
    public const int BeginConditionalFormatting = 461;
    /// <summary><c>BrtEndConditionalFormatting</c>.</summary>
    public const int EndConditionalFormatting = 462;
    /// <summary><c>BrtBeginCFRule</c>.</summary>
    public const int BeginCFRule = 463;
    /// <summary><c>BrtEndCFRule</c>.</summary>
    public const int EndCFRule = 464;
    /// <summary><c>BrtBeginIconSet</c>.</summary>
    public const int BeginIconSet = 465;
    /// <summary><c>BrtEndIconSet</c>.</summary>
    public const int EndIconSet = 466;
    /// <summary><c>BrtBeginDatabar</c>.</summary>
    public const int BeginDatabar = 467;
    /// <summary><c>BrtEndDatabar</c>.</summary>
    public const int EndDatabar = 468;
    /// <summary><c>BrtBeginColorScale</c>.</summary>
    public const int BeginColorScale = 469;
    /// <summary><c>BrtEndColorScale</c>.</summary>
    public const int EndColorScale = 470;
    /// <summary><c>BrtCFVO</c>.</summary>
    public const int CFVO = 471;
    /// <summary><c>BrtExternValueMeta</c>.</summary>
    public const int ExternValueMeta = 472;
    /// <summary><c>BrtBeginColorPalette</c>.</summary>
    public const int BeginColorPalette = 473;
    /// <summary><c>BrtEndColorPalette</c>.</summary>
    public const int EndColorPalette = 474;
    /// <summary><c>BrtIndexedColor</c>.</summary>
    public const int IndexedColor = 475;
    /// <summary><c>BrtMargins</c>.</summary>
    public const int Margins = 476;
    /// <summary><c>BrtPrintOptions</c>.</summary>
    public const int PrintOptions = 477;
    /// <summary><c>BrtPageSetup</c>.</summary>
    public const int PageSetup = 478;
    /// <summary><c>BrtBeginHeaderFooter</c>.</summary>
    public const int BeginHeaderFooter = 479;
    /// <summary><c>BrtEndHeaderFooter</c>.</summary>
    public const int EndHeaderFooter = 480;
    /// <summary><c>BrtBeginSXCrtFormat</c>.</summary>
    public const int BeginSXCrtFormat = 481;
    /// <summary><c>BrtEndSXCrtFormat</c>.</summary>
    public const int EndSXCrtFormat = 482;
    /// <summary><c>BrtBeginSXCrtFormats</c>.</summary>
    public const int BeginSXCrtFormats = 483;
    /// <summary><c>BrtEndSXCrtFormats</c>.</summary>
    public const int EndSXCrtFormats = 484;
    /// <summary><c>BrtWsFmtInfo</c>.</summary>
    public const int WsFmtInfo = 485;
    /// <summary><c>BrtBeginMgs</c>.</summary>
    public const int BeginMgs = 486;
    /// <summary><c>BrtEndMGs</c>.</summary>
    public const int EndMGs = 487;
    /// <summary><c>BrtBeginMGMaps</c>.</summary>
    public const int BeginMGMaps = 488;
    /// <summary><c>BrtEndMGMaps</c>.</summary>
    public const int EndMGMaps = 489;
    /// <summary><c>BrtBeginMG</c>.</summary>
    public const int BeginMG = 490;
    /// <summary><c>BrtEndMG</c>.</summary>
    public const int EndMG = 491;
    /// <summary><c>BrtBeginMap</c>.</summary>
    public const int BeginMap = 492;
    /// <summary><c>BrtEndMap</c>.</summary>
    public const int EndMap = 493;
    /// <summary><c>BrtHLink</c>.</summary>
    public const int HLink = 494;
    /// <summary><c>BrtBeginDCon</c>.</summary>
    public const int BeginDCon = 495;
    /// <summary><c>BrtEndDCon</c>.</summary>
    public const int EndDCon = 496;
    /// <summary><c>BrtBeginDRefs</c>.</summary>
    public const int BeginDRefs = 497;
    /// <summary><c>BrtEndDRefs</c>.</summary>
    public const int EndDRefs = 498;
    /// <summary><c>BrtDRef</c>.</summary>
    public const int DRef = 499;
    /// <summary><c>BrtBeginScenMan</c>.</summary>
    public const int BeginScenMan = 500;
    /// <summary><c>BrtEndScenMan</c>.</summary>
    public const int EndScenMan = 501;
    /// <summary><c>BrtBeginSct</c>.</summary>
    public const int BeginSct = 502;
    /// <summary><c>BrtEndSct</c>.</summary>
    public const int EndSct = 503;
    /// <summary><c>BrtSlc</c>.</summary>
    public const int Slc = 504;
    /// <summary><c>BrtBeginDXFs</c>.</summary>
    public const int BeginDXFs = 505;
    /// <summary><c>BrtEndDXFs</c>.</summary>
    public const int EndDXFs = 506;
    /// <summary><c>BrtDXF</c>.</summary>
    public const int DXF = 507;
    /// <summary><c>BrtBeginTableStyles</c>.</summary>
    public const int BeginTableStyles = 508;
    /// <summary><c>BrtEndTableStyles</c>.</summary>
    public const int EndTableStyles = 509;
    /// <summary><c>BrtBeginTableStyle</c>.</summary>
    public const int BeginTableStyle = 510;
    /// <summary><c>BrtEndTableStyle</c>.</summary>
    public const int EndTableStyle = 511;
    /// <summary><c>BrtTableStyleElement</c>.</summary>
    public const int TableStyleElement = 512;
    /// <summary><c>BrtTableStyleClient</c>.</summary>
    public const int TableStyleClient = 513;
    /// <summary><c>BrtBeginVolDeps</c>.</summary>
    public const int BeginVolDeps = 514;
    /// <summary><c>BrtEndVolDeps</c>.</summary>
    public const int EndVolDeps = 515;
    /// <summary><c>BrtBeginVolType</c>.</summary>
    public const int BeginVolType = 516;
    /// <summary><c>BrtEndVolType</c>.</summary>
    public const int EndVolType = 517;
    /// <summary><c>BrtBeginVolMain</c>.</summary>
    public const int BeginVolMain = 518;
    /// <summary><c>BrtEndVolMain</c>.</summary>
    public const int EndVolMain = 519;
    /// <summary><c>BrtBeginVolTopic</c>.</summary>
    public const int BeginVolTopic = 520;
    /// <summary><c>BrtEndVolTopic</c>.</summary>
    public const int EndVolTopic = 521;
    /// <summary><c>BrtVolSubtopic</c>.</summary>
    public const int VolSubtopic = 522;
    /// <summary><c>BrtVolRef</c>.</summary>
    public const int VolRef = 523;
    /// <summary><c>BrtVolNum</c>.</summary>
    public const int VolNum = 524;
    /// <summary><c>BrtVolErr</c>.</summary>
    public const int VolErr = 525;
    /// <summary><c>BrtVolStr</c>.</summary>
    public const int VolStr = 526;
    /// <summary><c>BrtVolBool</c>.</summary>
    public const int VolBool = 527;
    /// <summary><c>BrtBeginSortState</c>.</summary>
    public const int BeginSortState = 530;
    /// <summary><c>BrtEndSortState</c>.</summary>
    public const int EndSortState = 531;
    /// <summary><c>BrtBeginSortCond</c>.</summary>
    public const int BeginSortCond = 532;
    /// <summary><c>BrtEndSortCond</c>.</summary>
    public const int EndSortCond = 533;
    /// <summary><c>BrtBookProtection</c>.</summary>
    public const int BookProtection = 534;
    /// <summary><c>BrtSheetProtection</c>.</summary>
    public const int SheetProtection = 535;
    /// <summary><c>BrtRangeProtection</c>.</summary>
    public const int RangeProtection = 536;
    /// <summary><c>BrtPhoneticInfo</c>.</summary>
    public const int PhoneticInfo = 537;
    /// <summary><c>BrtBeginECTxtWiz</c>.</summary>
    public const int BeginECTxtWiz = 538;
    /// <summary><c>BrtEndECTxtWiz</c>.</summary>
    public const int EndECTxtWiz = 539;
    /// <summary><c>BrtBeginECTWFldInfoLst</c>.</summary>
    public const int BeginECTWFldInfoLst = 540;
    /// <summary><c>BrtEndECTWFldInfoLst</c>.</summary>
    public const int EndECTWFldInfoLst = 541;
    /// <summary><c>BrtBeginECTwFldInfo</c>.</summary>
    public const int BeginECTwFldInfo = 542;
    /// <summary><c>BrtFileSharing</c>.</summary>
    public const int FileSharing = 548;
    /// <summary><c>BrtOleSize</c>.</summary>
    public const int OleSize = 549;
    /// <summary><c>BrtDrawing</c>.</summary>
    public const int Drawing = 550;
    /// <summary><c>BrtLegacyDrawing</c>.</summary>
    public const int LegacyDrawing = 551;
    /// <summary><c>BrtLegacyDrawingHF</c>.</summary>
    public const int LegacyDrawingHF = 552;
    /// <summary><c>BrtWebOpt</c>.</summary>
    public const int WebOpt = 553;
    /// <summary><c>BrtBeginWebPubItems</c>.</summary>
    public const int BeginWebPubItems = 554;
    /// <summary><c>BrtEndWebPubItems</c>.</summary>
    public const int EndWebPubItems = 555;
    /// <summary><c>BrtBeginWebPubItem</c>.</summary>
    public const int BeginWebPubItem = 556;
    /// <summary><c>BrtEndWebPubItem</c>.</summary>
    public const int EndWebPubItem = 557;
    /// <summary><c>BrtBeginSXCondFmt</c>.</summary>
    public const int BeginSXCondFmt = 558;
    /// <summary><c>BrtEndSXCondFmt</c>.</summary>
    public const int EndSXCondFmt = 559;
    /// <summary><c>BrtBeginSXCondFmts</c>.</summary>
    public const int BeginSXCondFmts = 560;
    /// <summary><c>BrtEndSXCondFmts</c>.</summary>
    public const int EndSXCondFmts = 561;
    /// <summary><c>BrtBkHim</c>.</summary>
    public const int BkHim = 562;
    /// <summary><c>BrtColor</c>.</summary>
    public const int Color = 564;
    /// <summary><c>BrtBeginIndexedColors</c>.</summary>
    public const int BeginIndexedColors = 565;
    /// <summary><c>BrtEndIndexedColors</c>.</summary>
    public const int EndIndexedColors = 566;
    /// <summary><c>BrtBeginMRUColors</c>.</summary>
    public const int BeginMRUColors = 569;
    /// <summary><c>BrtEndMRUColors</c>.</summary>
    public const int EndMRUColors = 570;
    /// <summary><c>BrtMRUColor</c>.</summary>
    public const int MRUColor = 572;
    /// <summary><c>BrtBeginDVals</c>.</summary>
    public const int BeginDVals = 573;
    /// <summary><c>BrtEndDVals</c>.</summary>
    public const int EndDVals = 574;
    /// <summary><c>BrtSupNameStart</c>.</summary>
    public const int SupNameStart = 577;
    /// <summary><c>BrtSupNameValueStart</c>.</summary>
    public const int SupNameValueStart = 578;
    /// <summary><c>BrtSupNameValueEnd</c>.</summary>
    public const int SupNameValueEnd = 579;
    /// <summary><c>BrtSupNameNum</c>.</summary>
    public const int SupNameNum = 580;
    /// <summary><c>BrtSupNameErr</c>.</summary>
    public const int SupNameErr = 581;
    /// <summary><c>BrtSupNameSt</c>.</summary>
    public const int SupNameSt = 582;
    /// <summary><c>BrtSupNameNil</c>.</summary>
    public const int SupNameNil = 583;
    /// <summary><c>BrtSupNameBool</c>.</summary>
    public const int SupNameBool = 584;
    /// <summary><c>BrtSupNameFmla</c>.</summary>
    public const int SupNameFmla = 585;
    /// <summary><c>BrtSupNameBits</c>.</summary>
    public const int SupNameBits = 586;
    /// <summary><c>BrtSupNameEnd</c>.</summary>
    public const int SupNameEnd = 587;
    /// <summary><c>BrtEndSupBook</c>.</summary>
    public const int EndSupBook = 588;
    /// <summary><c>BrtCellSmartTagProperty</c>.</summary>
    public const int CellSmartTagProperty = 589;
    /// <summary><c>BrtBeginCellSmartTag</c>.</summary>
    public const int BeginCellSmartTag = 590;
    /// <summary><c>BrtEndCellSmartTag</c>.</summary>
    public const int EndCellSmartTag = 591;
    /// <summary><c>BrtBeginCellSmartTags</c>.</summary>
    public const int BeginCellSmartTags = 592;
    /// <summary><c>BrtEndCellSmartTags</c>.</summary>
    public const int EndCellSmartTags = 593;
    /// <summary><c>BrtBeginSmartTags</c>.</summary>
    public const int BeginSmartTags = 594;
    /// <summary><c>BrtEndSmartTags</c>.</summary>
    public const int EndSmartTags = 595;
    /// <summary><c>BrtSmartTagType</c>.</summary>
    public const int SmartTagType = 596;
    /// <summary><c>BrtBeginSmartTagTypes</c>.</summary>
    public const int BeginSmartTagTypes = 597;
    /// <summary><c>BrtEndSmartTagTypes</c>.</summary>
    public const int EndSmartTagTypes = 598;
    /// <summary><c>BrtBeginSXFilters</c>.</summary>
    public const int BeginSXFilters = 599;
    /// <summary><c>BrtEndSXFilters</c>.</summary>
    public const int EndSXFilters = 600;
    /// <summary><c>BrtBeginSXFILTER</c>.</summary>
    public const int BeginSXFILTER = 601;
    /// <summary><c>BrtEndSXFilter</c>.</summary>
    public const int EndSXFilter = 602;
    /// <summary><c>BrtBeginFills</c>.</summary>
    public const int BeginFills = 603;
    /// <summary><c>BrtEndFills</c>.</summary>
    public const int EndFills = 604;
    /// <summary><c>BrtBeginCellWatches</c>.</summary>
    public const int BeginCellWatches = 605;
    /// <summary><c>BrtEndCellWatches</c>.</summary>
    public const int EndCellWatches = 606;
    /// <summary><c>BrtCellWatch</c>.</summary>
    public const int CellWatch = 607;
    /// <summary><c>BrtBeginCRErrs</c>.</summary>
    public const int BeginCRErrs = 608;
    /// <summary><c>BrtEndCRErrs</c>.</summary>
    public const int EndCRErrs = 609;
    /// <summary><c>BrtCrashRecErr</c>.</summary>
    public const int CrashRecErr = 610;
    /// <summary><c>BrtBeginFonts</c>.</summary>
    public const int BeginFonts = 611;
    /// <summary><c>BrtEndFonts</c>.</summary>
    public const int EndFonts = 612;
    /// <summary><c>BrtBeginBorders</c>.</summary>
    public const int BeginBorders = 613;
    /// <summary><c>BrtEndBorders</c>.</summary>
    public const int EndBorders = 614;
    /// <summary><c>BrtBeginFmts</c>.</summary>
    public const int BeginFmts = 615;
    /// <summary><c>BrtEndFmts</c>.</summary>
    public const int EndFmts = 616;
    /// <summary><c>BrtBeginCellXFs</c>.</summary>
    public const int BeginCellXFs = 617;
    /// <summary><c>BrtEndCellXFs</c>.</summary>
    public const int EndCellXFs = 618;
    /// <summary><c>BrtBeginStyles</c>.</summary>
    public const int BeginStyles = 619;
    /// <summary><c>BrtEndStyles</c>.</summary>
    public const int EndStyles = 620;
    /// <summary><c>BrtBigName</c>.</summary>
    public const int BigName = 625;
    /// <summary><c>BrtBeginCellStyleXFs</c>.</summary>
    public const int BeginCellStyleXFs = 626;
    /// <summary><c>BrtEndCellStyleXFs</c>.</summary>
    public const int EndCellStyleXFs = 627;
    /// <summary><c>BrtBeginComments</c>.</summary>
    public const int BeginComments = 628;
    /// <summary><c>BrtEndComments</c>.</summary>
    public const int EndComments = 629;
    /// <summary><c>BrtBeginCommentAuthors</c>.</summary>
    public const int BeginCommentAuthors = 630;
    /// <summary><c>BrtEndCommentAuthors</c>.</summary>
    public const int EndCommentAuthors = 631;
    /// <summary><c>BrtCommentAuthor</c>.</summary>
    public const int CommentAuthor = 632;
    /// <summary><c>BrtBeginCommentList</c>.</summary>
    public const int BeginCommentList = 633;
    /// <summary><c>BrtEndCommentList</c>.</summary>
    public const int EndCommentList = 634;
    /// <summary><c>BrtBeginComment</c>.</summary>
    public const int BeginComment = 635;
    /// <summary><c>BrtEndComment</c>.</summary>
    public const int EndComment = 636;
    /// <summary><c>BrtCommentText</c>.</summary>
    public const int CommentText = 637;
    /// <summary><c>BrtBeginOleObjects</c>.</summary>
    public const int BeginOleObjects = 638;
    /// <summary><c>BrtOleObject</c>.</summary>
    public const int OleObject = 639;
    /// <summary><c>BrtEndOleObjects</c>.</summary>
    public const int EndOleObjects = 640;
    /// <summary><c>BrtBeginSxrules</c>.</summary>
    public const int BeginSxrules = 641;
    /// <summary><c>BrtEndSxRules</c>.</summary>
    public const int EndSxRules = 642;
    /// <summary><c>BrtBeginActiveXControls</c>.</summary>
    public const int BeginActiveXControls = 643;
    /// <summary><c>BrtActiveX</c>.</summary>
    public const int ActiveX = 644;
    /// <summary><c>BrtEndActiveXControls</c>.</summary>
    public const int EndActiveXControls = 645;
    /// <summary><c>BrtBeginCellIgnoreECs</c>.</summary>
    public const int BeginCellIgnoreECs = 648;
    /// <summary><c>BrtCellIgnoreEC</c>.</summary>
    public const int CellIgnoreEC = 649;
    /// <summary><c>BrtEndCellIgnoreECs</c>.</summary>
    public const int EndCellIgnoreECs = 650;
    /// <summary><c>BrtCsProp</c>.</summary>
    public const int CsProp = 651;
    /// <summary><c>BrtCsPageSetup</c>.</summary>
    public const int CsPageSetup = 652;
    /// <summary><c>BrtBeginUserCsViews</c>.</summary>
    public const int BeginUserCsViews = 653;
    /// <summary><c>BrtEndUserCsViews</c>.</summary>
    public const int EndUserCsViews = 654;
    /// <summary><c>BrtBeginUserCsView</c>.</summary>
    public const int BeginUserCsView = 655;
    /// <summary><c>BrtEndUserCsView</c>.</summary>
    public const int EndUserCsView = 656;
    /// <summary><c>BrtBeginPcdSFCIEntries</c>.</summary>
    public const int BeginPcdSFCIEntries = 657;
    /// <summary><c>BrtEndPCDSFCIEntries</c>.</summary>
    public const int EndPCDSFCIEntries = 658;
    /// <summary><c>BrtPCDSFCIEntry</c>.</summary>
    public const int PCDSFCIEntry = 659;
    /// <summary><c>BrtBeginListParts</c>.</summary>
    public const int BeginListParts = 660;
    /// <summary><c>BrtListPart</c>.</summary>
    public const int ListPart = 661;
    /// <summary><c>BrtEndListParts</c>.</summary>
    public const int EndListParts = 662;
    /// <summary><c>BrtSheetCalcProp</c>.</summary>
    public const int SheetCalcProp = 663;
    /// <summary><c>BrtBeginFnGroup</c>.</summary>
    public const int BeginFnGroup = 664;
    /// <summary><c>BrtFnGroup</c>.</summary>
    public const int FnGroup = 665;
    /// <summary><c>BrtEndFnGroup</c>.</summary>
    public const int EndFnGroup = 666;
    /// <summary><c>BrtSupAddin</c>.</summary>
    public const int SupAddin = 667;
    /// <summary><c>BrtSXTDMPOrder</c>.</summary>
    public const int SXTDMPOrder = 668;
    /// <summary><c>BrtCsProtection</c>.</summary>
    public const int CsProtection = 669;
    /// <summary><c>BrtBeginWsSortMap</c>.</summary>
    public const int BeginWsSortMap = 671;
    /// <summary><c>BrtEndWsSortMap</c>.</summary>
    public const int EndWsSortMap = 672;
    /// <summary><c>BrtBeginRRSort</c>.</summary>
    public const int BeginRRSort = 673;
    /// <summary><c>BrtEndRRSort</c>.</summary>
    public const int EndRRSort = 674;
    /// <summary><c>BrtRRSortItem</c>.</summary>
    public const int RRSortItem = 675;
    /// <summary><c>BrtFileSharingIso</c>.</summary>
    public const int FileSharingIso = 676;
    /// <summary><c>BrtBookProtectionIso</c>.</summary>
    public const int BookProtectionIso = 677;
    /// <summary><c>BrtSheetProtectionIso</c>.</summary>
    public const int SheetProtectionIso = 678;
    /// <summary><c>BrtCsProtectionIso</c>.</summary>
    public const int CsProtectionIso = 679;
    /// <summary><c>BrtRangeProtectionIso</c>.</summary>
    public const int RangeProtectionIso = 680;
    /// <summary><c>BrtDValList</c>.</summary>
    public const int DValList = 681;
    /// <summary><c>BrtRwDescent</c>.</summary>
    public const int RwDescent = 1024;
    /// <summary><c>BrtKnownFonts</c>.</summary>
    public const int KnownFonts = 1025;
    /// <summary><c>BrtBeginSXTupleSet</c>.</summary>
    public const int BeginSXTupleSet = 1026;
    /// <summary><c>BrtEndSXTupleSet</c>.</summary>
    public const int EndSXTupleSet = 1027;
    /// <summary><c>BrtBeginSXTupleSetHeader</c>.</summary>
    public const int BeginSXTupleSetHeader = 1028;
    /// <summary><c>BrtEndSXTupleSetHeader</c>.</summary>
    public const int EndSXTupleSetHeader = 1029;
    /// <summary><c>BrtSXTupleSetHeaderItem</c>.</summary>
    public const int SXTupleSetHeaderItem = 1030;
    /// <summary><c>BrtBeginSXTupleSetData</c>.</summary>
    public const int BeginSXTupleSetData = 1031;
    /// <summary><c>BrtEndSXTupleSetData</c>.</summary>
    public const int EndSXTupleSetData = 1032;
    /// <summary><c>BrtBeginSXTupleSetRow</c>.</summary>
    public const int BeginSXTupleSetRow = 1033;
    /// <summary><c>BrtEndSXTupleSetRow</c>.</summary>
    public const int EndSXTupleSetRow = 1034;
    /// <summary><c>BrtSXTupleSetRowItem</c>.</summary>
    public const int SXTupleSetRowItem = 1035;
    /// <summary><c>BrtNameExt</c>.</summary>
    public const int NameExt = 1036;
    /// <summary><c>BrtPCDH14</c>.</summary>
    public const int PCDH14 = 1037;
    /// <summary><c>BrtBeginPCDCalcMem14</c>.</summary>
    public const int BeginPCDCalcMem14 = 1038;
    /// <summary><c>BrtEndPCDCalcMem14</c>.</summary>
    public const int EndPCDCalcMem14 = 1039;
    /// <summary><c>BrtSXTH14</c>.</summary>
    public const int SXTH14 = 1040;
    /// <summary><c>BrtBeginSparklineGroup</c>.</summary>
    public const int BeginSparklineGroup = 1041;
    /// <summary><c>BrtEndSparklineGroup</c>.</summary>
    public const int EndSparklineGroup = 1042;
    /// <summary><c>BrtSparkline</c>.</summary>
    public const int Sparkline = 1043;
    /// <summary><c>BrtSXDI14</c>.</summary>
    public const int SXDI14 = 1044;
    /// <summary><c>BrtWsFmtInfoEx14</c>.</summary>
    public const int WsFmtInfoEx14 = 1045;
    /// <summary><c>BrtBeginCFRule14</c>.</summary>
    public const int BeginCFRule14 = 1048;
    /// <summary><c>BrtEndCFRule14</c>.</summary>
    public const int EndCFRule14 = 1049;
    /// <summary><c>BrtCFVO14</c>.</summary>
    public const int CFVO14 = 1050;
    /// <summary><c>BrtBeginDatabar14</c>.</summary>
    public const int BeginDatabar14 = 1051;
    /// <summary><c>BrtBeginIconSet14</c>.</summary>
    public const int BeginIconSet14 = 1052;
    /// <summary><c>BrtDVal14</c>.</summary>
    public const int DVal14 = 1053;
    /// <summary><c>BrtBeginDVals14</c>.</summary>
    public const int BeginDVals14 = 1054;
    /// <summary><c>BrtColor14</c>.</summary>
    public const int Color14 = 1055;
    /// <summary><c>BrtBeginSparklines</c>.</summary>
    public const int BeginSparklines = 1056;
    /// <summary><c>BrtEndSparklines</c>.</summary>
    public const int EndSparklines = 1057;
    /// <summary><c>BrtBeginSparklineGroups</c>.</summary>
    public const int BeginSparklineGroups = 1058;
    /// <summary><c>BrtEndSparklineGroups</c>.</summary>
    public const int EndSparklineGroups = 1059;
    /// <summary><c>BrtSXVD14</c>.</summary>
    public const int SXVD14 = 1061;
    /// <summary><c>BrtBeginSxView14</c>.</summary>
    public const int BeginSxView14 = 1062;
    /// <summary><c>BrtEndSxView14</c>.</summary>
    public const int EndSxView14 = 1063;
    /// <summary><c>BrtBeginSXView16</c>.</summary>
    public const int BeginSXView16 = 1064;
    /// <summary><c>BrtEndSXView16</c>.</summary>
    public const int EndSXView16 = 1065;
    /// <summary><c>BrtBeginPCD14</c>.</summary>
    public const int BeginPCD14 = 1066;
    /// <summary><c>BrtEndPCD14</c>.</summary>
    public const int EndPCD14 = 1067;
    /// <summary><c>BrtBeginExtConn14</c>.</summary>
    public const int BeginExtConn14 = 1068;
    /// <summary><c>BrtEndExtConn14</c>.</summary>
    public const int EndExtConn14 = 1069;
    /// <summary><c>BrtBeginSlicerCacheIDs</c>.</summary>
    public const int BeginSlicerCacheIDs = 1070;
    /// <summary><c>BrtEndSlicerCacheIDs</c>.</summary>
    public const int EndSlicerCacheIDs = 1071;
    /// <summary><c>BrtBeginSlicerCacheID</c>.</summary>
    public const int BeginSlicerCacheID = 1072;
    /// <summary><c>BrtEndSlicerCacheID</c>.</summary>
    public const int EndSlicerCacheID = 1073;
    /// <summary><c>BrtBeginSlicerCache</c>.</summary>
    public const int BeginSlicerCache = 1075;
    /// <summary><c>BrtEndSlicerCache</c>.</summary>
    public const int EndSlicerCache = 1076;
    /// <summary><c>BrtBeginSlicerCacheDef</c>.</summary>
    public const int BeginSlicerCacheDef = 1077;
    /// <summary><c>BrtEndSlicerCacheDef</c>.</summary>
    public const int EndSlicerCacheDef = 1078;
    /// <summary><c>BrtBeginSlicersEx</c>.</summary>
    public const int BeginSlicersExt = 1079;
    /// <summary><c>BrtEndSlicersEx</c>.</summary>
    public const int EndSlicersExt = 1080;
    /// <summary><c>BrtBeginSlicerEx</c>.</summary>
    public const int BeginSlicerExt = 1081;
    /// <summary><c>BrtEndSlicerEx</c>.</summary>
    public const int EndSlicerExt = 1082;
    /// <summary><c>BrtBeginSlicer</c>.</summary>
    public const int BeginSlicer = 1083;
    /// <summary><c>BrtEndSlicer</c>.</summary>
    public const int EndSlicer = 1084;
    /// <summary><c>BrtSlicerCachePivotTables</c>.</summary>
    public const int SlicerCachePivotTables = 1085;
    /// <summary><c>BrtBeginSlicerCacheOlapImpl</c>.</summary>
    public const int BeginSlicerCacheOlapImplementation = 1086;
    /// <summary><c>BrtEndSlicerCacheOlapImpl</c>.</summary>
    public const int EndSlicerCacheOlapImplementation = 1087;
    /// <summary><c>BrtBeginSlicerCacheLevelsData</c>.</summary>
    public const int BeginSlicerCacheLevelsData = 1088;
    /// <summary><c>BrtEndSlicerCacheLevelsData</c>.</summary>
    public const int EndSlicerCacheLevelsData = 1089;
    /// <summary><c>BrtBeginSlicerCacheLevelData</c>.</summary>
    public const int BeginSlicerCacheLevelData = 1090;
    /// <summary><c>BrtEndSlicerCacheLevelData</c>.</summary>
    public const int EndSlicerCacheLevelData = 1091;
    /// <summary><c>BrtBeginSlicerCacheSiRanges</c>.</summary>
    public const int BeginSlicerCacheSiRanges = 1092;
    /// <summary><c>BrtEndSlicerCacheSiRanges</c>.</summary>
    public const int EndSlicerCacheSiRanges = 1093;
    /// <summary><c>BrtBeginSlicerCacheSiRange</c>.</summary>
    public const int BeginSlicerCacheSiRange = 1094;
    /// <summary><c>BrtEndSlicerCacheSiRange</c>.</summary>
    public const int EndSlicerCacheSiRange = 1095;
    /// <summary><c>BrtSlicerCacheOlapItem</c>.</summary>
    public const int SlicerCacheOlapItem = 1096;
    /// <summary><c>BrtBeginSlicerCacheSelections</c>.</summary>
    public const int BeginSlicerCacheSelections = 1097;
    /// <summary><c>BrtSlicerCacheSelection</c>.</summary>
    public const int SlicerCacheSelection = 1098;
    /// <summary><c>BrtEndSlicerCacheSelections</c>.</summary>
    public const int EndSlicerCacheSelections = 1099;
    /// <summary><c>BrtBeginSlicerCacheNative</c>.</summary>
    public const int BeginSlicerCacheNative = 1100;
    /// <summary><c>BrtEndSlicerCacheNative</c>.</summary>
    public const int EndSlicerCacheNative = 1101;
    /// <summary><c>BrtSlicerCacheNativeItem</c>.</summary>
    public const int SlicerCacheNativeItem = 1102;
    /// <summary><c>BrtRangeProtection14</c>.</summary>
    public const int RangeProtection14 = 1103;
    /// <summary><c>BrtRangeProtectionIso14</c>.</summary>
    public const int RangeProtectionIso14 = 1104;
    /// <summary><c>BrtCellIgnoreEC14</c>.</summary>
    public const int CellIgnoreEC14 = 1105;
    /// <summary><c>BrtList14</c>.</summary>
    public const int List14 = 1111;
    /// <summary><c>BrtCFIcon</c>.</summary>
    public const int CFIcon = 1112;
    /// <summary><c>BrtBeginSlicers</c>.</summary>
    public const int BeginSlicers = 1115;
    /// <summary><c>BrtEndSlicers</c>.</summary>
    public const int EndSlicers = 1116;
    /// <summary><c>BrtWbProp14</c>.</summary>
    public const int WbProp14 = 1117;
    /// <summary><c>BrtBeginSXEdit</c>.</summary>
    public const int BeginSXEdit = 1118;
    /// <summary><c>BrtEndSXEdit</c>.</summary>
    public const int EndSXEdit = 1119;
    /// <summary><c>BrtBeginSXEdits</c>.</summary>
    public const int BeginSXEdits = 1120;
    /// <summary><c>BrtEndSXEdits</c>.</summary>
    public const int EndSXEdits = 1121;
    /// <summary><c>BrtBeginSXChange</c>.</summary>
    public const int BeginSXChange = 1122;
    /// <summary><c>BrtEndSXChange</c>.</summary>
    public const int EndSXChange = 1123;
    /// <summary><c>BrtBeginSXChanges</c>.</summary>
    public const int BeginSXChanges = 1124;
    /// <summary><c>BrtEndSXChanges</c>.</summary>
    public const int EndSXChanges = 1125;
    /// <summary><c>BrtSXTupleItems</c>.</summary>
    public const int SXTupleItems = 1126;
    /// <summary><c>BrtBeginSlicerStyle</c>.</summary>
    public const int BeginSlicerStyle = 1128;
    /// <summary><c>BrtEndSlicerStyle</c>.</summary>
    public const int EndSlicerStyle = 1129;
    /// <summary><c>BrtSlicerStyleElement</c>.</summary>
    public const int SlicerStyleElement = 1130;
    /// <summary><c>BrtBeginStyleSheetExt14</c>.</summary>
    public const int BeginStyleSheetExt14 = 1131;
    /// <summary><c>BrtEndStyleSheetExt14</c>.</summary>
    public const int EndStyleSheetExt14 = 1132;
    /// <summary><c>BrtEndConditionalFormattings</c>.</summary>
    public const int EndConditionalFormattings = 1136;
    /// <summary><c>BrtBeginPCDCalcMemExt</c>.</summary>
    public const int BeginPCDCalcMemExt = 1137;
    /// <summary><c>BrtEndPCDCalcMemExt</c>.</summary>
    public const int EndPCDCalcMemExt = 1138;
    /// <summary><c>BrtBeginPCDCalcMemsExt</c>.</summary>
    public const int BeginPCDCalcMemsExt = 1139;
    /// <summary><c>BrtEndPCDCalcMemsExt</c>.</summary>
    public const int EndPCDCalcMemsExt = 1140;
    /// <summary><c>BrtPCDField14</c>.</summary>
    public const int PCDField14 = 1141;
    /// <summary><c>BrtBeginSlicerStyles</c>.</summary>
    public const int BeginSlicerStyles = 1142;
    /// <summary><c>BrtEndSlicerStyles</c>.</summary>
    public const int EndSlicerStyles = 1143;
    /// <summary><c>BrtBeginSlicerStyleElements</c>.</summary>
    public const int BeginSlicerStyleElements = 1144;
    /// <summary><c>BrtEndSlicerStyleElements</c>.</summary>
    public const int EndSlicerStyleElements = 1145;
    /// <summary><c>BrtCFRuleExt</c>.</summary>
    public const int CFRuleExt = 1146;
    /// <summary><c>BrtBeginSXCondFmt14</c>.</summary>
    public const int BeginSXCondFmt14 = 1147;
    /// <summary><c>BrtEndSXCondFmt14</c>.</summary>
    public const int EndSXCondFmt14 = 1148;
    /// <summary><c>BrtBeginSXCondFmts14</c>.</summary>
    public const int BeginSXCondFmts14 = 1149;
    /// <summary><c>BrtEndSXCondFmts14</c>.</summary>
    public const int EndSXCondFmts14 = 1150;
    /// <summary><c>BrtBeginSortCond14</c>.</summary>
    public const int BeginSortCond14 = 1152;
    /// <summary><c>BrtEndSortCond14</c>.</summary>
    public const int EndSortCond14 = 1153;
    /// <summary><c>BrtEndDVals14</c>.</summary>
    public const int EndDVals14 = 1154;
    /// <summary><c>BrtEndIconSet14</c>.</summary>
    public const int EndIconSet14 = 1155;
    /// <summary><c>BrtEndDatabar14</c>.</summary>
    public const int EndDatabar14 = 1156;
    /// <summary><c>BrtBeginColorScale14</c>.</summary>
    public const int BeginColorScale14 = 1157;
    /// <summary><c>BrtEndColorScale14</c>.</summary>
    public const int EndColorScale14 = 1158;
    /// <summary><c>BrtBeginSxrules14</c>.</summary>
    public const int BeginSxrules14 = 1159;
    /// <summary><c>BrtEndSxrules14</c>.</summary>
    public const int EndSxrules14 = 1160;
    /// <summary><c>BrtBeginPRule14</c>.</summary>
    public const int BeginPRule14 = 1161;
    /// <summary><c>BrtEndPRule14</c>.</summary>
    public const int EndPRule14 = 1162;
    /// <summary><c>BrtBeginPRFilters14</c>.</summary>
    public const int BeginPRFilters14 = 1163;
    /// <summary><c>BrtEndPRFilters14</c>.</summary>
    public const int EndPRFilters14 = 1164;
    /// <summary><c>BrtBeginPRFilter14</c>.</summary>
    public const int BeginPRFilter14 = 1165;
    /// <summary><c>BrtEndPRFilter14</c>.</summary>
    public const int EndPRFilter14 = 1166;
    /// <summary><c>BrtBeginPRFItem14</c>.</summary>
    public const int BeginPRFItem14 = 1167;
    /// <summary><c>BrtEndPRFItem14</c>.</summary>
    public const int EndPRFItem14 = 1168;
    /// <summary><c>BrtBeginCellIgnoreECs14</c>.</summary>
    public const int BeginCellIgnoreECs14 = 1169;
    /// <summary><c>BrtEndCellIgnoreECs14</c>.</summary>
    public const int EndCellIgnoreECs14 = 1170;
    /// <summary><c>BrtDXF14</c>.</summary>
    public const int DXF14 = 1171;
    /// <summary><c>BrtBeginDxF14s</c>.</summary>
    public const int BeginDxF14s = 1172;
    /// <summary><c>BrtEndDxf14s</c>.</summary>
    public const int EndDxf14s = 1173;
    /// <summary><c>BrtFilter14</c>.</summary>
    public const int Filter14 = 1177;
    /// <summary><c>BrtBeginCustomFilters14</c>.</summary>
    public const int BeginCustomFilters14 = 1178;
    /// <summary><c>BrtCustomFilter14</c>.</summary>
    public const int CustomFilter14 = 1180;
    /// <summary><c>BrtIconFilter14</c>.</summary>
    public const int IconFilter14 = 1181;
    /// <summary><c>BrtPivotCacheConnectionName</c>.</summary>
    public const int PivotCacheConnectionName = 1182;
    /// <summary><c>BrtDecoupledPivotCacheID</c>.</summary>
    public const int DecoupledPivotCacheID = 2048;
    /// <summary><c>BrtBeginPivotTableRefs</c>.</summary>
    public const int BeginPivotTableRefs = 2051;
    /// <summary><c>BrtEndPivotTableRefs</c>.</summary>
    public const int EndPivotTableRefs = 2052;
    /// <summary><c>BrtPivotTableRef</c>.</summary>
    public const int PivotTableRef = 2053;
    /// <summary><c>BrtSlicerCacheBookPivotTables</c>.</summary>
    public const int SlicerCacheBookPivotTables = 2054;
    /// <summary><c>BrtBeginSxvcells</c>.</summary>
    public const int BeginSxvcells = 2055;
    /// <summary><c>BrtEndSxvcells</c>.</summary>
    public const int EndSxvcells = 2056;
    /// <summary><c>BrtBeginSxRow</c>.</summary>
    public const int BeginSxRow = 2057;
    /// <summary><c>BrtEndSxRow</c>.</summary>
    public const int EndSxRow = 2058;
    /// <summary><c>BrtPCDCalcMem15</c>.</summary>
    public const int PCDCalcMem15 = 2060;
    /// <summary><c>BrtQsi15</c>.</summary>
    public const int Qsi15 = 2067;
    /// <summary><c>BrtBeginWebExtensions</c>.</summary>
    public const int BeginWebExtensions = 2068;
    /// <summary><c>BrtEndWebExtensions</c>.</summary>
    public const int EndWebExtensions = 2069;
    /// <summary><c>BrtWebExtension</c>.</summary>
    public const int WebExtension = 2070;
    /// <summary><c>BrtAbsPath15</c>.</summary>
    public const int AbsPath15 = 2071;
    /// <summary><c>BrtBeginPivotTableUISettings</c>.</summary>
    public const int BeginPivotTableUISettings = 2072;
    /// <summary><c>BrtEndPivotTableUISettings</c>.</summary>
    public const int EndPivotTableUISettings = 2073;
    /// <summary><c>BrtTableSlicerCacheIDs</c>.</summary>
    public const int TableSlicerCacheIDs = 2075;
    /// <summary><c>BrtTableSlicerCacheID</c>.</summary>
    public const int TableSlicerCacheID = 2076;
    /// <summary><c>BrtBeginTableSlicerCache</c>.</summary>
    public const int BeginTableSlicerCache = 2077;
    /// <summary><c>BrtEndTableSlicerCache</c>.</summary>
    public const int EndTableSlicerCache = 2078;
    /// <summary><c>BrtSxFilter15</c>.</summary>
    public const int SxFilter15 = 2079;
    /// <summary><c>BrtTimelineCachePivotCacheID</c>.</summary>
    public const int TimelineCachePivotCacheID = 2082;
    /// <summary><c>BrtBeginTimelineCacheIDs</c>.</summary>
    public const int BeginTimelineCacheIDs = 2083;
    /// <summary><c>BrtEndTimelineCacheIDs</c>.</summary>
    public const int EndTimelineCacheIDs = 2084;
    /// <summary><c>BrtBeginTimelineCacheID</c>.</summary>
    public const int BeginTimelineCacheID = 2085;
    /// <summary><c>BrtEndTimelineCacheID</c>.</summary>
    public const int EndTimelineCacheID = 2086;
    /// <summary><c>BrtBeginTimelinesEx</c>.</summary>
    public const int BeginTimelinesExt = 2087;
    /// <summary><c>BrtEndTimelinesEx</c>.</summary>
    public const int EndTimelinesExt = 2088;
    /// <summary><c>BrtBeginTimelineEx</c>.</summary>
    public const int BeginTimelineExt = 2089;
    /// <summary><c>BrtEndTimelineEx</c>.</summary>
    public const int EndTimelineExt = 2090;
    /// <summary><c>BrtWorkBookPr15</c>.</summary>
    public const int WorkBookPr15 = 2091;
    /// <summary><c>BrtPCDH15</c>.</summary>
    public const int PCDH15 = 2092;
    /// <summary><c>BrtBeginTimelineStyle</c>.</summary>
    public const int BeginTimelineStyle = 2093;
    /// <summary><c>BrtEndTimelineStyle</c>.</summary>
    public const int EndTimelineStyle = 2094;
    /// <summary><c>BrtTimelineStyleElement</c>.</summary>
    public const int TimelineStyleElement = 2095;
    /// <summary><c>BrtEndTimelineStylesheetExt15</c>.</summary>
    public const int EndTimelineStylesheetExt15 = 2097;
    /// <summary><c>BrtBeginTimelineStyles</c>.</summary>
    public const int BeginTimelineStyles = 2098;
    /// <summary><c>BrtEndTimelineStyles</c>.</summary>
    public const int EndTimelineStyles = 2099;
    /// <summary><c>BrtBeginTimelineStyleElements</c>.</summary>
    public const int BeginTimelineStyleElements = 2100;
    /// <summary><c>BrtEndTimelineStyleElements</c>.</summary>
    public const int EndTimelineStyleElements = 2101;
    /// <summary><c>BrtDXF15</c>.</summary>
    public const int DXF15 = 2102;
    /// <summary><c>BrtBeginDXFs15</c>.</summary>
    public const int BeginDXFs15 = 2103;
    /// <summary><c>BrtEndDXFs15</c>.</summary>
    public const int EndDXFs15 = 2104;
    /// <summary><c>BrtBeginItemUniqueNames</c>.</summary>
    public const int BeginItemUniqueNames = 2106;
    /// <summary><c>BrtEndItemUniqueNames</c>.</summary>
    public const int EndItemUniqueNames = 2107;
    /// <summary><c>BrtItemUniqueName</c>.</summary>
    public const int ItemUniqueName = 2108;
    /// <summary><c>BrtBeginExtConn15</c>.</summary>
    public const int BeginExtConn15 = 2109;
    /// <summary><c>BrtEndExtConn15</c>.</summary>
    public const int EndExtConn15 = 2110;
    /// <summary><c>BrtBeginOledbPr15</c>.</summary>
    public const int BeginOledbPr15 = 2111;
    /// <summary><c>BrtEndOledbPr15</c>.</summary>
    public const int EndOledbPr15 = 2112;
    /// <summary><c>BrtBeginDataFeedPr15</c>.</summary>
    public const int BeginDataFeedPr15 = 2113;
    /// <summary><c>BrtEndDataFeedPr15</c>.</summary>
    public const int EndDataFeedPr15 = 2114;
    /// <summary><c>BrtTextPr15</c>.</summary>
    public const int TextPr15 = 2115;
    /// <summary><c>BrtRangePr15</c>.</summary>
    public const int RangePr15 = 2116;
    /// <summary><c>BrtDbCommand15</c>.</summary>
    public const int DbCommand15 = 2117;
    /// <summary><c>BrtBeginDbTables15</c>.</summary>
    public const int BeginDbTables15 = 2118;
    /// <summary><c>BrtEndDbTables15</c>.</summary>
    public const int EndDbTables15 = 2119;
    /// <summary><c>BrtDbTable15</c>.</summary>
    public const int DbTable15 = 2120;
    /// <summary><c>BrtBeginDataModel</c>.</summary>
    public const int BeginDataModel = 2121;
    /// <summary><c>BrtEndDataModel</c>.</summary>
    public const int EndDataModel = 2122;
    /// <summary><c>BrtBeginModelTables</c>.</summary>
    public const int BeginModelTables = 2123;
    /// <summary><c>BrtEndModelTables</c>.</summary>
    public const int EndModelTables = 2124;
    /// <summary><c>BrtModelTable</c>.</summary>
    public const int ModelTable = 2125;
    /// <summary><c>BrtBeginModelRelationships</c>.</summary>
    public const int BeginModelRelationships = 2126;
    /// <summary><c>BrtEndModelRelationships</c>.</summary>
    public const int EndModelRelationships = 2127;
    /// <summary><c>BrtModelRelationship</c>.</summary>
    public const int ModelRelationship = 2128;
    /// <summary><c>BrtBeginECTxtWiz15</c>.</summary>
    public const int BeginECTxtWiz15 = 2129;
    /// <summary><c>BrtEndECTxtWiz15</c>.</summary>
    public const int EndECTxtWiz15 = 2130;
    /// <summary><c>BrtBeginECTWFldInfoLst15</c>.</summary>
    public const int BeginECTWFldInfoLst15 = 2131;
    /// <summary><c>BrtEndECTWFldInfoLst15</c>.</summary>
    public const int EndECTWFldInfoLst15 = 2132;
    /// <summary><c>BrtBeginECTwFldInfo15</c>.</summary>
    public const int BeginECTwFldInfo15 = 2133;
    /// <summary><c>BrtFieldListActiveItem</c>.</summary>
    public const int FieldListActiveItem = 2134;
    /// <summary><c>BrtPivotCacheIdVersion</c>.</summary>
    public const int PivotCacheIdVersion = 2135;
    /// <summary><c>BrtSXDI15</c>.</summary>
    public const int SXDI15 = 2136;
    /// <summary><c>BrtBeginDynamicArrayPr</c>.</summary>
    public const int BeginDynamicArrayPr = 4096;
    /// <summary><c>BrtEndDynamicArrayPr</c>.</summary>
    public const int EndDynamicArrayPr = 4097;
    /// <summary><c>BrtBeginRichValueBlock</c>.</summary>
    public const int BeginRichValueBlock = 5002;
    /// <summary><c>BrtEndRichValueBlock</c>.</summary>
    public const int EndRichValueBlock = 5003;
    /// <summary><c>BrtBeginRichFilters</c>.</summary>
    public const int BeginRichFilters = 5081;
    /// <summary><c>BrtEndRichFilters</c>.</summary>
    public const int EndRichFilters = 5082;
    /// <summary><c>BrtRichFilter</c>.</summary>
    public const int RichFilter = 5083;
    /// <summary><c>BrtBeginRichFilterColumn</c>.</summary>
    public const int BeginRichFilterColumn = 5084;
    /// <summary><c>BrtEndRichFilterColumn</c>.</summary>
    public const int EndRichFilterColumn = 5085;
    /// <summary><c>BrtBeginCustomRichFilters</c>.</summary>
    public const int BeginCustomRichFilters = 5086;
    /// <summary><c>BrtEndCustomRichFilters</c>.</summary>
    public const int EndCustomRichFilters = 5087;
    /// <summary><c>BrtTop10RichFilter</c>.</summary>
    public const int Top10RichFilter = 5089;
    /// <summary><c>BrtDynamicRichFilter</c>.</summary>
    public const int DynamicRichFilter = 5090;
    /// <summary><c>BrtBeginRichSortCondition</c>.</summary>
    public const int BeginRichSortCondition = 5092;
    /// <summary><c>BrtEndRichSortCondition</c>.</summary>
    public const int EndRichSortCondition = 5093;
    /// <summary><c>BrtRichFilterDateGroupItem</c>.</summary>
    public const int RichFilterDateGroupItem = 5094;
    /// <summary><c>BrtBeginCalcFeatures</c>.</summary>
    public const int BeginCalcFeatures = 5095;
    /// <summary><c>BrtEndCalcFeatures</c>.</summary>
    public const int EndCalcFeatures = 5096;
    /// <summary><c>BrtCalcFeature</c>.</summary>
    public const int CalcFeature = 5097;
    /// <summary><c>BrtExternalLinksPr</c>.</summary>
    public const int ExternalLinksPr = 5099;
    /// <summary><c>BrtPivotFieldIgnorableAfter</c>.</summary>
    public const int PivotFieldIgnorableAfter = 5101;
    /// <summary><c>BrtPivotDataFieldFutureData</c>.</summary>
    public const int PivotDataFieldFutureData = 5103;
    /// <summary><c>BrtPivotCacheRichData</c>.</summary>
    public const int PivotCacheRichData = 5105;
    /// <summary><c>BrtExternalLinksAlternateUrls</c>.</summary>
    public const int ExternalLinksAlternateUrls = 5108;
    /// <summary><c>BrtBeginPivotVersionInfo</c>.</summary>
    public const int BeginPivotVersionInfo = 5109;
    /// <summary><c>BrtEndPivotVersionInfo</c>.</summary>
    public const int EndPivotVersionInfo = 5110;
    /// <summary><c>BrtBeginCacheVersionInfo</c>.</summary>
    public const int BeginCacheVersionInfo = 5111;
    /// <summary><c>BrtEndCacheVersionInfo</c>.</summary>
    public const int EndCacheVersionInfo = 5112;
    /// <summary><c>BrtPivotRequiredFeature</c>.</summary>
    public const int PivotRequiredFeature = 5113;
    /// <summary><c>BrtPivotLastUsedFeature</c>.</summary>
    public const int PivotLastUsedFeature = 5114;
    /// <summary><c>BrtExternalCodeService</c>.</summary>
    public const int ExternalCodeService = 5117;
    /// <summary><c>BrtShowDataTypeIcons</c>.</summary>
    public const int ShowDataTypeIcons = 5127;
    /// <summary><c>BrtSXDIAggregation</c>.</summary>
    public const int SXDIAggregation = 5130;
    /// <summary><c>BrtPivotCacheAutoRefresh</c>.</summary>
    public const int PivotCacheAutoRefresh = 5132;
    /// <summary><c>BrtPivotCacheDataSource</c>.</summary>
    public const int PivotCacheDataSource = 5133;
    /// <summary><c>BrtBeginSxvdSubtotals</c>.</summary>
    public const int BeginSxvdSubtotals = 5136;
    /// <summary><c>BrtEndSxvdSubtotals</c>.</summary>
    public const int EndSxvdSubtotals = 5137;
    /// <summary><c>BrtSxvdSubtotal</c>.</summary>
    public const int SxvdSubtotal = 5138;
    /// <summary><c>BrtEndPivotRuleFilterSubtotals</c>.</summary>
    public const int EndPivotRuleFilterSubtotals = 5140;
    /// <summary><c>BrtPivotRuleFilterSubtotal</c>.</summary>
    public const int PivotRuleFilterSubtotal = 5141;
    /// <summary><c>BrtEndSxvdSubtotalLineItems</c>.</summary>
    public const int EndSxvdSubtotalLineItems = 5144;
    /// <summary><c>BrtSxvdSubtotalLineItem</c>.</summary>
    public const int SxvdSubtotalLineItem = 5145;
    /// <summary><c>BrtPivotCacheDataHasHeaders</c>.</summary>
    public const int PivotCacheDataHasHeaders = 5146;
    /// <summary><c>BrtPivotCacheDataSourceClean</c>.</summary>
    public const int PivotCacheDataSourceClean = 5153;
}
