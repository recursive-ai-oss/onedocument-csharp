using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.NumberFormats;

/// <summary>How a locale counts years for the <c>e</c>, <c>g</c> and <c>b</c> date codes.</summary>
public enum EraStyle
{
    /// <summary>Gregorian years; <c>e</c> is the four-digit year.</summary>
    Gregorian = 0,

    /// <summary>Japanese imperial eras (Meiji, Taisho, Showa, Heisei, Reiwa).</summary>
    Japanese = 1,

    /// <summary>Republic of China (Minguo) years: the Gregorian year minus 1911.</summary>
    Minguo = 2,

    /// <summary>Thai Buddhist years: the Gregorian year plus 543.</summary>
    Buddhist = 3,
}

/// <summary>
/// The locale data number formatting needs: separators, month and day names, AM/PM, currency and the patterns behind locale-dependent
/// built-in formats. The data is compiled into the library (it never reads the host's culture), so output is the same on every
/// platform, with ICU or without, in invariant-globalization mode and in the browser.
/// </summary>
/// <remarks>
/// The names follow the Windows/Office conventions for each locale (for example German abbreviates March as "Mrz"). Lookups by
/// language identifier use <see cref="Get"/>; an unknown identifier falls back to <see cref="EnglishUnitedStates"/>. The table covers
/// en-US, en-GB, de-DE, fr-FR, es-ES, it-IT, pt-BR, nl-NL, ru-RU, ja-JP, zh-CN, zh-TW and ko-KR.
/// </remarks>
public sealed class NumberFormatCulture
{
    private readonly string[] _months;
    private readonly string[] _monthsAbbr;
    private readonly string[] _monthsGenitive;
    private readonly string[] _days;
    private readonly string[] _daysAbbr;

    private NumberFormatCulture(
        int lcid, string name, string dec, string grp, string months, string monthsAbbr, string days, string daysAbbr,
        string am, string pm, string currency, bool currencyAfter, string shortDate, string longDate, string longTime, EraStyle era,
        string? monthsGenitive = null)
    {
        Lcid = lcid;
        Name = name;
        DecimalSeparator = dec;
        GroupSeparator = grp;
        _months = months.Split('|');
        _monthsGenitive = monthsGenitive?.Split('|') ?? _months;
        _monthsAbbr = monthsAbbr.Split('|');
        _days = days.Split('|');
        _daysAbbr = daysAbbr.Split('|');
        AmDesignator = am;
        PmDesignator = pm;
        CurrencySymbol = currency;
        CurrencyAfterNumber = currencyAfter;
        ShortDatePattern = shortDate;
        LongDatePattern = longDate;
        LongTimePattern = longTime;
        EraStyle = era;
    }

    /// <summary>The Windows language identifier (LCID) such as 0x0409.</summary>
    public int Lcid { get; }

    /// <summary>The BCP 47 name such as <c>en-US</c>.</summary>
    public string Name { get; }

    /// <summary>The decimal separator.</summary>
    public string DecimalSeparator { get; }

    /// <summary>The thousands separator.</summary>
    public string GroupSeparator { get; }

    /// <summary>The text shown for times before noon.</summary>
    public string AmDesignator { get; }

    /// <summary>The text shown for times from noon.</summary>
    public string PmDesignator { get; }

    /// <summary>The local currency symbol used by the locale-dependent built-in currency formats.</summary>
    public string CurrencySymbol { get; }

    /// <summary>Whether the currency symbol follows the number (with a space) in the locale's currency formats.</summary>
    public bool CurrencyAfterNumber { get; }

    /// <summary>The format code behind built-in format 14 (short date), in Excel code syntax.</summary>
    public string ShortDatePattern { get; }

    /// <summary>The format code the <c>[$-F800]</c> system long date stands for.</summary>
    public string LongDatePattern { get; }

    /// <summary>The format code the <c>[$-F400]</c> system long time stands for.</summary>
    public string LongTimePattern { get; }

    /// <summary>How the locale counts years for the <c>e</c>, <c>g</c> and <c>b</c> date codes.</summary>
    public EraStyle EraStyle { get; }

    /// <summary>US English (0x0409), the default.</summary>
    public static NumberFormatCulture EnglishUnitedStates => All[0];

    /// <summary>The invariant choice: the same data as US English.</summary>
    public static NumberFormatCulture Invariant => All[0];

    /// <summary>Returns the culture for a language identifier.</summary>
    /// <param name="lcid">The LCID, with any calendar or numeral-system bits already removed (only the low 16 bits are used).</param>
    /// <returns>The matching culture, or US English when the table has none.</returns>
    public static NumberFormatCulture Get(int lcid) => TryGet(lcid, out NumberFormatCulture? c) && c is not null ? c : All[0];

    /// <summary>Looks up a culture by language identifier.</summary>
    /// <param name="lcid">The LCID; only the low 16 bits are used. Another region of a language in the table (such as German in Austria, 0x0C07) matches the table's entry for that language.</param>
    /// <param name="culture">The culture when found.</param>
    /// <returns><see langword="true"/> when the table has the locale.</returns>
    public static bool TryGet(int lcid, out NumberFormatCulture? culture)
    {
        int id = lcid & 0xFFFF;
        foreach (NumberFormatCulture c in All)
        {
            if (c.Lcid == id)
            {
                culture = c;
                return true;
            }
        }

        int primary = id & 0x3FF;
        foreach (NumberFormatCulture c in All)
        {
            if ((c.Lcid & 0x3FF) == primary)
            {
                culture = c;
                return true;
            }
        }

        culture = null;
        return false;
    }

    /// <summary>Returns a month name.</summary>
    /// <param name="month">1 to 12.</param>
    /// <param name="abbreviated">Whether to return the abbreviation.</param>
    /// <returns>The name.</returns>
    public string GetMonthName(int month, bool abbreviated) => (abbreviated ? _monthsAbbr : _months)[Math.Clamp(month, 1, 12) - 1];

    /// <summary>Returns the full month name as it is written next to a day number (no locale in the table needs a different form; Excel uses the nominative name).</summary>
    /// <param name="month">1 to 12.</param>
    /// <returns>The name.</returns>
    public string GetMonthNameWithDay(int month) => _monthsGenitive[Math.Clamp(month, 1, 12) - 1];

    /// <summary>Returns a weekday name.</summary>
    /// <param name="dayOfWeek">0 for Sunday to 6 for Saturday.</param>
    /// <param name="abbreviated">Whether to return the abbreviation.</param>
    /// <returns>The name.</returns>
    public string GetDayName(int dayOfWeek, bool abbreviated) => (abbreviated ? _daysAbbr : _days)[Math.Clamp(dayOfWeek, 0, 6)];

    private static readonly NumberFormatCulture[] All =
    [
        new(0x0409, "en-US", ".", ",",
            "January|February|March|April|May|June|July|August|September|October|November|December",
            "Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec",
            "Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday", "Sun|Mon|Tue|Wed|Thu|Fri|Sat",
            "AM", "PM", "$", false, "m/d/yyyy", "dddd, mmmm d, yyyy", "h:mm:ss AM/PM", EraStyle.Gregorian),
        new(0x0809, "en-GB", ".", ",",
            "January|February|March|April|May|June|July|August|September|October|November|December",
            "Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec",
            "Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday", "Sun|Mon|Tue|Wed|Thu|Fri|Sat",
            "AM", "PM", "£", false, "dd/mm/yyyy", "dddd, d mmmm yyyy", "hh:mm:ss", EraStyle.Gregorian),
        new(0x0407, "de-DE", ",", ".",
            "Januar|Februar|März|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember",
            "Jan|Feb|Mrz|Apr|Mai|Jun|Jul|Aug|Sep|Okt|Nov|Dez",
            "Sonntag|Montag|Dienstag|Mittwoch|Donnerstag|Freitag|Samstag", "So|Mo|Di|Mi|Do|Fr|Sa",
            "AM", "PM", "€", true, "dd.mm.yyyy", "dddd, d. mmmm yyyy", "hh:mm:ss", EraStyle.Gregorian),
        new(0x040C, "fr-FR", ",", "\u202F",
            "janvier|février|mars|avril|mai|juin|juillet|août|septembre|octobre|novembre|décembre",
            "janv.|févr.|mars|avr.|mai|juin|juil.|août|sept.|oct.|nov.|déc.",
            "dimanche|lundi|mardi|mercredi|jeudi|vendredi|samedi", "dim.|lun.|mar.|mer.|jeu.|ven.|sam.",
            "AM", "PM", "€", true, "dd/mm/yyyy", "dddd d mmmm yyyy", "hh:mm:ss", EraStyle.Gregorian),
        new(0x0C0A, "es-ES", ",", ".",
            "enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|octubre|noviembre|diciembre",
            "ene.|feb.|mar.|abr.|may.|jun.|jul.|ago.|sep.|oct.|nov.|dic.",
            "domingo|lunes|martes|miércoles|jueves|viernes|sábado", "do.|lu.|ma.|mi.|ju.|vi.|sá.",
            "AM", "PM", "€", true, "d/m/yyyy", "dddd, d \"de\" mmmm \"de\" yyyy", "h:mm:ss", EraStyle.Gregorian),
        new(0x0410, "it-IT", ",", ".",
            "gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre",
            "gen|feb|mar|apr|mag|giu|lug|ago|set|ott|nov|dic",
            "domenica|lunedì|martedì|mercoledì|giovedì|venerdì|sabato", "dom|lun|mar|mer|gio|ven|sab",
            "AM", "PM", "€", true, "dd/mm/yyyy", "dddd d mmmm yyyy", "hh:mm:ss", EraStyle.Gregorian),
        new(0x0416, "pt-BR", ",", ".",
            "janeiro|fevereiro|março|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro",
            "jan|fev|mar|abr|mai|jun|jul|ago|set|out|nov|dez",
            "domingo|segunda-feira|terça-feira|quarta-feira|quinta-feira|sexta-feira|sábado", "dom|seg|ter|qua|qui|sex|sáb",
            "AM", "PM", "R$", false, "dd/mm/yyyy", "dddd, d \"de\" mmmm \"de\" yyyy", "hh:mm:ss", EraStyle.Gregorian),
        new(0x0413, "nl-NL", ",", ".",
            "januari|februari|maart|april|mei|juni|juli|augustus|september|oktober|november|december",
            "jan|feb|mrt|apr|mei|jun|jul|aug|sep|okt|nov|dec",
            "zondag|maandag|dinsdag|woensdag|donderdag|vrijdag|zaterdag", "zo|ma|di|wo|do|vr|za",
            "AM", "PM", "€", false, "d-m-yyyy", "dddd d mmmm yyyy", "h:mm:ss", EraStyle.Gregorian),
        new(0x0419, "ru-RU", ",", "\u00A0",
            "Январь|Февраль|Март|Апрель|Май|Июнь|Июль|Август|Сентябрь|Октябрь|Ноябрь|Декабрь",
            "янв|фев|мар|апр|май|июн|июл|авг|сен|окт|ноя|дек",
            "воскресенье|понедельник|вторник|среда|четверг|пятница|суббота", "Вс|Пн|Вт|Ср|Чт|Пт|Сб",
            "AM", "PM", "₽", true, "dd.mm.yyyy", "d mmmm yyyy \"г.\"", "h:mm:ss", EraStyle.Gregorian),
        new(0x0411, "ja-JP", ".", ",",
            "1月|2月|3月|4月|5月|6月|7月|8月|9月|10月|11月|12月",
            "1|2|3|4|5|6|7|8|9|10|11|12",
            "日曜日|月曜日|火曜日|水曜日|木曜日|金曜日|土曜日", "日|月|火|水|木|金|土",
            "午前", "午後", "¥", false, "yyyy/m/d", "yyyy\"年\"m\"月\"d\"日\"", "h:mm:ss", EraStyle.Japanese),
        new(0x0804, "zh-CN", ".", ",",
            "一月|二月|三月|四月|五月|六月|七月|八月|九月|十月|十一月|十二月",
            "1月|2月|3月|4月|5月|6月|7月|8月|9月|10月|11月|12月",
            "星期日|星期一|星期二|星期三|星期四|星期五|星期六", "周日|周一|周二|周三|周四|周五|周六",
            "上午", "下午", "¥", false, "yyyy/m/d", "yyyy\"年\"m\"月\"d\"日\"", "h:mm:ss", EraStyle.Gregorian),
        new(0x0404, "zh-TW", ".", ",",
            "一月|二月|三月|四月|五月|六月|七月|八月|九月|十月|十一月|十二月",
            "一月|二月|三月|四月|五月|六月|七月|八月|九月|十月|十一月|十二月",
            "星期日|星期一|星期二|星期三|星期四|星期五|星期六", "週日|週一|週二|週三|週四|週五|週六",
            "上午", "下午", "NT$", false, "yyyy/m/d", "yyyy\"年\"m\"月\"d\"日\"", "AM/PM hh:mm:ss", EraStyle.Gregorian),
        new(0x0412, "ko-KR", ".", ",",
            "1월|2월|3월|4월|5월|6월|7월|8월|9월|10월|11월|12월",
            "1|2|3|4|5|6|7|8|9|10|11|12",
            "일요일|월요일|화요일|수요일|목요일|금요일|토요일", "일|월|화|수|목|금|토",
            "오전", "오후", "₩", false, "yyyy-mm-dd", "yyyy\"년\" m\"월\" d\"일\" dddd", "AM/PM h:mm:ss", EraStyle.Gregorian),
        new(0x041E, "th-TH", ".", ",",
            "มกราคม|กุมภาพันธ์|มีนาคม|เมษายน|พฤษภาคม|มิถุนายน|กรกฎาคม|สิงหาคม|กันยายน|ตุลาคม|พฤศจิกายน|ธันวาคม",
            "ม.ค.|ก.พ.|มี.ค.|เม.ย.|พ.ค.|มิ.ย.|ก.ค.|ส.ค.|ก.ย.|ต.ค.|พ.ย.|ธ.ค.",
            "อาทิตย์|จันทร์|อังคาร|พุธ|พฤหัสบดี|ศุกร์|เสาร์", "อา.|จ.|อ.|พ.|พฤ.|ศ.|ส.",
            "AM", "PM", "฿", false, "d/m/yyyy", "d mmmm yyyy", "h:mm:ss", EraStyle.Gregorian),
    ];

    /// <summary>The cultures in the table.</summary>
    public static IReadOnlyList<NumberFormatCulture> Supported => All;
}
