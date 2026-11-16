namespace MemoLex.Core;

/// <summary>Mnemonic labels and selection guidance follow MaiMemo's category definitions.</summary>
public static class MnemonicTypes
{
    public static IReadOnlyList<string> All {get;}=System.Array.AsReadOnly(new[]
    {
        "词根词缀","固定搭配","辨析","语法","联想","合成","近反义词",
        "派生","词源","谐音","串记","口诀","扩展","其他"
    });

    public static bool IsAllowed(string? type)=>type!=null&&All.Contains(type,StringComparer.Ordinal);

    // Only old labels are mapped here. Existing standard labels remain the author's choice.
    // A mnemonic with an unknown label keeps its content under the fallback category.
    public static string Normalize(string? type)=>type?.Trim() switch
    {
        {} value when IsAllowed(value)=>value,
        "词根"=>"词根词缀",
        "固搭"=>"固定搭配",
        "对比"=>"辨析",
        "近义" or "反义"=>"近反义词",
        "谐音联想" or "声音联想" or "谐音与对比联想"=>"谐音",
        "语法联想" or "字形与语法联想" or "词形联想"=>"语法",
        "词源联想" or "专名联想" or "缩合联想"=>"词源",
        "亲称联想"=>"派生",
        "缩写联想" or "数字联想"=>"扩展",
        "拆词联想" or "形近词联想" or "多义联想" or "字形联想" or
        "情景联想" or "词义联想" or "拼写联想" or "符号联想" or
        "动作联想" or "构词联想"=>"联想",
        _=>"其他"
    };

    public static string Description(string? type)=>Normalize(type) switch
    {
        "词根词缀"=>"解析词根、词缀，也可扩展同根词。以独立单词为词干添加词缀，选「派生」。",
        "固定搭配"=>"常用词组和固定搭配，如 at lunch、fast food。",
        "辨析"=>"比较易混用的近义词，说明含义或用法的区别。",
        "语法"=>"说明这个单词的语法规则、句中位置或词形变化。",
        "联想"=>"借助其他单词、字形或熟悉的场景帮助记忆。人为拆词也归此类；谐音优先选「谐音」。",
        "合成"=>"由两个或多个独立单词真正组合而成，如 class＋room。人为拆词选「联想」。",
        "近反义词"=>"列出近义词或反义词；需要比较具体用法时选「辨析」。",
        "派生"=>"以已有的独立单词为词干，添加前缀或后缀，也可扩展相关派生词。",
        "词源"=>"说明单词的来源、历史背景或相关故事。",
        "谐音"=>"利用相近的发音帮助记忆；同时包含联想时，优先归入此类。",
        "串记"=>"将意思不相关、字母组合相似的一组单词串联记忆。",
        "口诀"=>"用顺口、易记的短句或口诀帮助记忆。",
        "扩展"=>"补充释义或相关知识。短语、词源、同根词和近反义词应归入对应类别。",
        _=>"不适合以上分类时使用，一般用不到。"
    };
}
