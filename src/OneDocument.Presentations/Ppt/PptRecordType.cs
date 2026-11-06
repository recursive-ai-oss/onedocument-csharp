namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>Record types of the PowerPoint binary format ([MS-PPT] §2.13.24) that the reader dispatches on.</summary>
public static class PptRecordType
{
    /// <summary>RT_Document: the document container.</summary>
    public const int Document = 0x03E8;

    /// <summary>RT_DocumentAtom.</summary>
    public const int DocumentAtom = 0x03E9;

    /// <summary>RT_EndDocumentAtom.</summary>
    public const int EndDocumentAtom = 0x03EA;

    /// <summary>RT_Slide: a slide or title master.</summary>
    public const int Slide = 0x03EE;

    /// <summary>RT_SlideAtom.</summary>
    public const int SlideAtom = 0x03EF;

    /// <summary>RT_Notes: a notes slide or notes master.</summary>
    public const int Notes = 0x03F0;

    /// <summary>RT_NotesAtom.</summary>
    public const int NotesAtom = 0x03F1;

    /// <summary>RT_Environment.</summary>
    public const int Environment = 0x03F2;

    /// <summary>RT_SlidePersistAtom.</summary>
    public const int SlidePersistAtom = 0x03F3;

    /// <summary>RT_MainMaster.</summary>
    public const int MainMaster = 0x03F8;

    /// <summary>RT_SlideShowSlideInfoAtom: the transition of a slide.</summary>
    public const int SlideShowSlideInfoAtom = 0x03F9;

    /// <summary>RT_VbaInfo.</summary>
    public const int VbaInfo = 0x03FF;

    /// <summary>RT_ExternalObjectList.</summary>
    public const int ExternalObjectList = 0x0409;

    /// <summary>RT_DrawingGroup.</summary>
    public const int DrawingGroup = 0x040B;

    /// <summary>RT_Drawing (the slide's PPDrawing).</summary>
    public const int Drawing = 0x040C;

    /// <summary>RT_RoundTripTheme12Atom.</summary>
    public const int RoundTripTheme12 = 0x040E;

    /// <summary>RT_List: the font collection's neighbour, an unused list container.</summary>
    public const int List = 0x07D0;

    /// <summary>RT_FontCollection.</summary>
    public const int FontCollection = 0x07D5;

    /// <summary>RT_ColorSchemeAtom.</summary>
    public const int ColorSchemeAtom = 0x07F0;

    /// <summary>RT_ExternalObjectRefAtom.</summary>
    public const int ExternalObjectRefAtom = 0x0BC1;

    /// <summary>RT_PlaceholderAtom.</summary>
    public const int PlaceholderAtom = 0x0BC3;

    /// <summary>RT_OutlineTextRefAtom.</summary>
    public const int OutlineTextRefAtom = 0x0F9E;

    /// <summary>RT_TextHeaderAtom.</summary>
    public const int TextHeaderAtom = 0x0F9F;

    /// <summary>RT_TextCharsAtom.</summary>
    public const int TextCharsAtom = 0x0FA0;

    /// <summary>RT_StyleTextPropAtom.</summary>
    public const int StyleTextPropAtom = 0x0FA1;

    /// <summary>RT_MasterTextPropAtom.</summary>
    public const int MasterTextPropAtom = 0x0FA2;

    /// <summary>RT_TextMasterStyleAtom.</summary>
    public const int TextMasterStyleAtom = 0x0FA3;

    /// <summary>RT_TextCharFormatExceptionAtom.</summary>
    public const int TextCharFormatExceptionAtom = 0x0FA4;

    /// <summary>RT_TextParagraphFormatExceptionAtom.</summary>
    public const int TextParagraphFormatExceptionAtom = 0x0FA5;

    /// <summary>RT_TextRulerAtom.</summary>
    public const int TextRulerAtom = 0x0FA6;

    /// <summary>RT_TextBookmarkAtom.</summary>
    public const int TextBookmarkAtom = 0x0FA7;

    /// <summary>RT_TextBytesAtom.</summary>
    public const int TextBytesAtom = 0x0FA8;

    /// <summary>RT_TextSpecialInfoAtom.</summary>
    public const int TextSpecialInfoAtom = 0x0FAA;

    /// <summary>RT_DefaultRulerAtom.</summary>
    public const int DefaultRulerAtom = 0x0FAB;

    /// <summary>RT_FontEntityAtom.</summary>
    public const int FontEntityAtom = 0x0FB7;

    /// <summary>RT_CString.</summary>
    public const int CString = 0x0FBA;

    /// <summary>RT_MetaFile (an OLE preview, among others).</summary>
    public const int MetaFile = 0x0FC1;

    /// <summary>RT_ExternalOleObjectAtom.</summary>
    public const int ExternalOleObjectAtom = 0x0FC3;

    /// <summary>RT_Handout: the handout master.</summary>
    public const int Handout = 0x0FC9;

    /// <summary>RT_ExternalOleObjectStg: the storage of an OLE object, a persist object.</summary>
    public const int ExternalOleObjectStg = 0x1011;

    /// <summary>RT_ExternalOleEmbed.</summary>
    public const int ExternalOleEmbed = 0x0FCC;

    /// <summary>RT_ExternalOleEmbedAtom.</summary>
    public const int ExternalOleEmbedAtom = 0x0FCD;

    /// <summary>RT_ExternalOleLink.</summary>
    public const int ExternalOleLink = 0x0FCE;

    /// <summary>RT_ExternalHyperlinkAtom.</summary>
    public const int ExternalHyperlinkAtom = 0x0FD3;

    /// <summary>RT_ExternalHyperlink.</summary>
    public const int ExternalHyperlink = 0x0FD7;

    /// <summary>RT_SlideNumberMetaCharAtom.</summary>
    public const int SlideNumberMetaCharAtom = 0x0FD8;

    /// <summary>RT_HeadersFooters.</summary>
    public const int HeadersFooters = 0x0FD9;

    /// <summary>RT_HeadersFootersAtom.</summary>
    public const int HeadersFootersAtom = 0x0FDA;

    /// <summary>RT_TextInteractiveInfoAtom.</summary>
    public const int TextInteractiveInfoAtom = 0x0FDF;

    /// <summary>RT_SlideListWithText.</summary>
    public const int SlideListWithText = 0x0FF0;

    /// <summary>RT_AnimationInfoAtom.</summary>
    public const int AnimationInfoAtom = 0x0FF1;

    /// <summary>RT_InteractiveInfo.</summary>
    public const int InteractiveInfo = 0x0FF2;

    /// <summary>RT_InteractiveInfoAtom.</summary>
    public const int InteractiveInfoAtom = 0x0FF3;

    /// <summary>RT_UserEditAtom.</summary>
    public const int UserEditAtom = 0x0FF5;

    /// <summary>RT_CurrentUserAtom.</summary>
    public const int CurrentUserAtom = 0x0FF6;

    /// <summary>RT_DateTimeMetaCharAtom.</summary>
    public const int DateTimeMetaCharAtom = 0x0FF7;

    /// <summary>RT_GenericDateMetaCharAtom.</summary>
    public const int GenericDateMetaCharAtom = 0x0FF8;

    /// <summary>RT_HeaderMetaCharAtom.</summary>
    public const int HeaderMetaCharAtom = 0x0FF9;

    /// <summary>RT_FooterMetaCharAtom.</summary>
    public const int FooterMetaCharAtom = 0x0FFA;

    /// <summary>RT_AnimationInfo.</summary>
    public const int AnimationInfo = 0x1014;

    /// <summary>RT_RtfDateTimeMetaCharAtom.</summary>
    public const int RtfDateTimeMetaCharAtom = 0x1015;

    /// <summary>RT_ProgTags.</summary>
    public const int ProgTags = 0x1388;

    /// <summary>RT_PersistDirectoryAtom.</summary>
    public const int PersistDirectoryAtom = 0x1772;

    /// <summary>RT_Comment10.</summary>
    public const int Comment10 = 0x2EE0;

    /// <summary>RT_Comment10Atom.</summary>
    public const int Comment10Atom = 0x2EE1;

    /// <summary>RT_CommentIndex10.</summary>
    public const int CommentIndex10 = 0x2EE4;

    /// <summary>RT_CryptSession10Container.</summary>
    public const int CryptSession10Container = 0x2F14;
}
