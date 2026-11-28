namespace MemoLex.Core;

public sealed record ConfusionWordHook(string Word, string Hook, string? HighlightPart = null);

public sealed record ConfusionGroup(
    string SharedAnchor,
    ConfusionWordHook[] Members);

/// <summary>
/// High-frequency confusable word groups with memory-first differentiation hooks
/// (root/affix breakdown, visual letter cues, and associative mnemonics).
/// </summary>
public static class WordConfusionBank
{
    private static readonly ConfusionGroup[] Groups =
    [
        new("共同词根 sta / stat（站立、立足）· 抓后缀一眼分清", [
            new("statue", "statu + e → 雕像（站着不动）｜e 像一只鹅的雕塑", "e"),
            new("status", "statu + s → 地位、状态（站的位置）｜s 像地位曲曲折折上上下下", "s"),
            new("stature", "statu + re → 声望、身材（在人们心中站着的形象）｜re 记作「在人们心中」", "re"),
            new("statute", "statu + te → 法规、法令（国家立足的根本）｜te 记作「特别」，针对特别事项立法", "te"),
            new("station", "sta + tion → 车站、驻地（站着等车/驻守的地方）", "tion"),
            new("static", "sta + tic → 静止的、静电（保持站着不动）", "tic"),
            new("state", "stat + e → 状态、国家、陈述（站稳确立的局面）", "te")
        ]),
        new("ki- / kid- 形近组 · 拆开前后两截直接记住", [
            new("kidnap", "kid（孩子）+ nap（打盹）→ 趁孩子打盹偷偷带走 → 绑架、诱拐", "nap"),
            new("kinship", "kin（亲属、同族）+ -ship（关系）→ 亲属关系、血缘关系", "nship"),
            new("kidney", "kid（小孩）+ ney（谐音“泥”）→ 小孩用泥捏的一对豆形器官 → 肾脏", "ney"),
            new("kindred", "kin（亲属）+ -dred → 同族的、血缘相近的", "ndred"),
            new("kindle", "kind（火种柴火）+ -le → 点燃、激起（感情）", "ndle"),
            new("kinda", "kind of 的口语连读缩略 → 有点儿、稍微", "nda")
        ]),
        new("ad + a/o/e + pt · 只看中间一个元音字母", [
            new("adapt", "ad + apt（适合）→ 适应、改编｜a 记作「all around 到处适应」", "a"),
            new("adopt", "ad + opt（选择）→ 采纳、收养｜o 像圆圈「圈选出来」", "o"),
            new("adept", "ad + ept → 熟练的、内行的｜e 记作「expert 专家」", "e")
        ]),
        new("单 s 与双 ss · 多一个 s 多一份甜", [
            new("desert", "单 s 荒凉缺水 → 沙漠；把人丢在荒处 → 遗弃、抛弃", "s"),
            new("dessert", "双 ss（sweet sugar 双倍甜）→ 餐后甜点", "ss")
        ]),
        new("a / e / ef 词首之差 · 动作、结果与用力", [
            new("affect", "a 记作 act（动作）→ vt. 影响、感动", "a"),
            new("effect", "e 记作 end（结果）→ n. 效果、影响", "e"),
            new("effort", "ef（出）+ fort（力量）→ 使出力量 → 努力", "ef")
        ]),
        new("pal 与 ple 词尾 · 记「人」还是记「规则」", [
            new("principal", "结尾 pal（伙伴 = 人）→ 校长；主要的、首要的", "pal"),
            new("principle", "结尾 -ple 同 rule（规则）→ 原则、原理", "ple")
        ]),
        new("ary 与 ery 一字之差 · 停住 vs 信封", [
            new("stationary", "a 记作 at rest（停在原地）→ 静止的、不动的", "a"),
            new("stationery", "e 记作 envelope（信封）→ 文具、信纸", "e")
        ]),
        new("i 与 e 一字之差 · 赞美你 vs 补完整", [
            new("compliment", "i 记作「I like you」→ 赞美、恭维", "i"),
            new("complement", "e 记作「complete 完整」→ 补充、补足", "e")
        ]),
        new("qui- 三兄弟 · 看结尾 et / te / t", [
            new("quiet", "结尾 et（e 在 t 前，闭嘴安静）→ 安静的、轻声的", "et"),
            new("quite", "结尾 te（t 顶在前面，程度拉满）→ 相当、完全", "te"),
            new("quit", "最短没有 e，干脆停掉 → 退出、放弃、戒掉", "t")
        ]),
        new("双 oo 与单 o · 空间松动 vs 少了一个", [
            new("loose", "双 oo 空间宽敞松动 → 松的、宽松的", "oo"),
            new("lose", "单 o 丢掉了一个 o → 丢失、输掉", "o")
        ]),
        new("ai 与 ia 顺序 · 放牛呼吸空气 vs 天天写日记", [
            new("dairy", "d + air（户外空气放牛）→ 乳制品、奶牛场", "ai"),
            new("diary", "di（day 每天）+ ary → 日记、日记本", "ia")
        ]),
        new("el 与 le 顺序 · 展开双翅 vs 直角夹角", [
            new("angel", "结尾 el 像展开的翅膀 → 天使", "el"),
            new("angle", "结尾 le 像直角 L 的拐角 → 角度、视角", "le")
        ]),
        new("ar 与 er 词尾 · 祭台架子 vs 动手改动", [
            new("altar", "ar 像高台支架 → 祭坛、圣坛", "ar"),
            new("alter", "er 动词动作 → 改变、修改", "er")
        ]),
        new("broad（宽）与 board（板）· 字母 o/a 位置决定意思", [
            new("abroad", "a + broad（广阔海外）→ 在国外、到国外", "broad"),
            new("aboard", "a + board（登上船板/机舱）→ 在船/飞机/车上", "board"),
            new("broad", "ro 开口宽大 → 宽阔的、广泛的", "ro"),
            new("board", "oar（船桨）配木板 → 木板、登机、董事会", "oa")
        ]),
        new("cil 与 sel 词尾 · 市政委员会 vs 给建议的人", [
            new("council", "cil 联想 city（市政厅）→ 委员会、议会", "cil"),
            new("counsel", "sel 联想 tell（告知劝说）→ 劝告、忠告、法律顾问", "sel")
        ]),
        new("共同词根 migr（迁移）· 看前缀 e 出 / im 入", [
            new("emigrate", "e-（向外）+ migrate → 移居国外、迁出", "e"),
            new("immigrate", "im-（向内）+ migrate → 移居入境、迁入", "im"),
            new("migrate", "词根 migr（移动）→ 迁徙、移居", "migr")
        ]),
        new("共同词根 min（突出、逼近）· 看前缀区分远近", [
            new("eminent", "e-（向外突出）→ 杰出的、显赫的", "e"),
            new("imminent", "im-（压在眼前）→ 迫在眉睫的、即将发生的", "im"),
            new("prominent", "pro-（向前凸出）→ 显著的、突出的、著名的", "pro")
        ]),
        new("e- 与 il- 前缀 · 引出来 vs 不合法", [
            new("elicit", "e-（引出）+ licit → 引出、套出（回答/反应）", "e"),
            new("illicit", "il-（否定）+ licit（合法）→ 非法的、违禁的", "il")
        ]),
        new("e- 与 il- + 词根 lig/leg · 选得中 vs 读不出", [
            new("eligible", "e-（出）+ lig（选择）→ 能被选中的 → 合格的、有资格的", "e"),
            new("illegible", "il-（不）+ leg（读）→ 读不出来的 → 字迹模糊难辨的", "il")
        ]),
        new("de- / dis- 辨形 · 得体、下降与异议", [
            new("decent", "dec（得体）→ 体面的、像样的、正派的", "dec"),
            new("descent", "de-（向下）+ scend（爬）→ 下降、血统出身", "sc"),
            new("dissent", "dis-（分歧）+ sent（感觉/想法）→ 不同意见、异议", "ss")
        ]),
        new("少 e 与多 e · 道德原则 vs 鼓舞的能量", [
            new("moral", "无 e → 道德的、寓意", "al"),
            new("morale", "多一个 e（联想 energy 能量）→ 士气、斗志", "ale")
        ]),
        new("单 n 与双 nn · 个人属性 vs 集体人员", [
            new("personal", "-al 形容词尾 → 个人的、私人的", "al"),
            new("personnel", "双 nn + el（两个人手聚在一起）→ 全体人员、人事部门", "nnel")
        ]),
        new("pre-（在前）与 pro-（向前）+ 词根 ced（走）", [
            new("precede", "pre-（在前面）+ cede（走）→ 先于、在…之前", "pre"),
            new("proceed", "pro-（向前）+ ceed（走）→ 继续进行、前进", "pro")
        ]),
        new("pro- 与 per- + secute（追随）· 当庭起诉 vs 步步迫害", [
            new("prosecute", "pro-（在法庭前）+ secute → 起诉、控告", "pro"),
            new("persecute", "per-（一直追着不放）+ secute → 迫害、烦扰", "per")
        ]),
        new("firm（坚固）与 form（形状）· 抓核心词根", [
            new("confirm", "con- + firm（坚固）→ 让事实站稳 → 确认、证实", "firm"),
            new("conform", "con-（共同）+ form（形状）→ 形状保持一致 → 遵守、符合", "form")
        ]),
        new("共同词根 quire（寻求）· 看前缀 ac / in / re", [
            new("acquire", "ac-（去拿到）+ quire → 经过努力拿到手 → 获得、习得", "ac"),
            new("inquire", "in-（向内探问）+ quire → 打听、询问、调查", "in"),
            new("require", "re-（反复索要）+ quire → 需要、要求", "re")
        ]),
        new("ex- + pand / pend / tend · 摊开、花钱、拉长", [
            new("expand", "ex- + pand（联想平底锅 pan 摊平）→ 扩张、展开", "pand"),
            new("expend", "ex- + pend（同 spend 花钱）→ 花费、消耗", "pend"),
            new("extend", "ex- + tend（伸展拉长）→ 延伸、延长、提供", "tend")
        ]),
        new("-ic 与 -ical · 领域属性 vs 精打细算", [
            new("economic", "-ic 学科/领域 → 经济的、经济学的", "ic"),
            new("economical", "-ical（联想 cal 计算着花钱）→ 节俭的、省钱的、划算的", "ical")
        ]),
        new("respect 三兄弟 · 看后缀 -able / -ful / -ive", [
            new("respectable", "-able（值得被…）→ 值得尊敬的、体面的", "able"),
            new("respectful", "-ful（充满敬意）→ 恭敬的、有礼貌的", "ful"),
            new("respective", "-ive（各自对应的）→ 各自的、分别的", "ive")
        ]),
        new("consider 双胞胎 · 看后缀 -able / -ate", [
            new("considerable", "-able（大到值得考虑）→ 相当大的、可观的", "able"),
            new("considerate", "-ate（替别人着想的）→ 体贴的、考虑周到的", "ate")
        ]),
        new("sens- 双胞胎 · 理性判断 vs 敏锐易受刺激", [
            new("sensible", "-ible（有理智常识的）→ 明智的、合情理的", "ible"),
            new("sensitive", "-itive（对外界反应强烈）→ 敏感的、灵敏的", "itive")
        ]),
        new("imagin- 三兄弟 · 看后缀 -able / -ary / -ative", [
            new("imaginable", "-able（能被想到）→ 可想象的", "able"),
            new("imaginary", "-ary（虚构出来的）→ 虚构的、假想的", "ary"),
            new("imaginative", "-ative（富有想象力）→ 富有想象力的", "ative")
        ]),
        new("-ual 与 -uous · 时断时续 vs 连绵不断", [
            new("continual", "-ual（反复停了又来）→ 频繁的、时断时续的", "ual"),
            new("continuous", "-uous（u-o-u 连成一线）→ 连续不断的", "uous")
        ]),
        new("-ic 与 -ical · 载入史册 vs 历史相关", [
            new("historic", "-ic → 历史上著名的、具有重大历史意义的", "ic"),
            new("historical", "-ical → 历史上的、历史学的（如历史资料）", "ical")
        ]),
        new("-ial 与 -ious · 工业的 vs 勤奋的", [
            new("industrial", "industry + -al → 工业的、产业的", "ial"),
            new("industrious", "-ious（us 拼命干活）→ 勤奋的、刻苦的", "ious")
        ]),
        new("intense 与 intensive · 程度强烈 vs 集中密集", [
            new("intense", "词根 tens（拉紧）→ 强烈的、剧烈的、紧张的", "e"),
            new("intensive", "-ive（密集投入）→ 密集的、集中的、精深的", "ive")
        ]),
        new("liter-（文字）三兄弟 · 逐字、文学、识字", [
            new("literal", "-al → 字面上的、逐字的", "al"),
            new("literary", "-ary（文学艺术）→ 文学的、书面的", "ary"),
            new("literate", "-ate（受过教育的）→ 识字的、有文化的", "ate")
        ]),
        new("neglig-（忽视）双胞胎 · 人疏忽 vs 事微小", [
            new("negligent", "-ent（形容人粗心）→ 疏忽的、失职的", "ent"),
            new("negligible", "-ible（小到可以忽略）→ 微不足道的、可忽略的", "ible")
        ]),
        new("per-（穿透看）与 pro-（向前看）+ spective", [
            new("perspective", "per-（穿透看）→ 视角、观点、透视法", "per"),
            new("prospective", "pro-（向前看）→ 预期的、未来的、潜在的", "pro")
        ]),
        new("sure（确定）三兄弟 · 看前缀 as / en / in", [
            new("assure", "as-（向人说）→ 向某人保证、使确信", "as"),
            new("ensure", "en-（使成为）→ 确保、保证（某事发生）", "en"),
            new("insure", "in-（入保）→ 投保、给…上保险", "in")
        ]),
        new("rise（升起）四兄弟 · 区分及物与不及物", [
            new("arise", "a- + rise（vi.）→ 出现、产生（问题/机会）", "a"),
            new("arouse", "a- + rouse（vt.）→ 唤起、激起（兴趣/情绪）", "ou"),
            new("rise", "vi. 自己升起 → 上升、上涨（无宾语）", "i"),
            new("raise", "vt. 动手抬起 → 举起、提高、筹集（后接宾语）", "ai")
        ]),
        new("良心（名词）与清醒（形容词）", [
            new("conscience", "con- + science（内心的知晓）→ 良心、道德心", "ience"),
            new("conscious", "-ous 形容词尾 → 有意识的、清醒的", "ious")
        ]),
        new("costume（服装）与 custom（习俗）", [
            new("costume", "多一个 e（联想舞台戏服）→ 服装、戏装", "e"),
            new("custom", "无 e（日常惯例）→ 习俗、惯例；复数表海关", "om")
        ]),
        new("-ce（名词）与 -se（动词）", [
            new("device", "-ce 名词尾 → 装置、设备、手段", "ce"),
            new("devise", "-se 动词尾 → 设计、发明、想出", "se")
        ]),
        new("o（晒干成洞）与 a（air 气流）", [
            new("drought", "中间 o 像被烈日晒干的圆坑 → 干旱、旱灾", "o"),
            new("draught", "中间 a 联想 air（空气气流）→ 穿堂风、草图", "a")
        ]),
        new("ex-（外露）与 im-（内含）+ plicit（折叠）", [
            new("explicit", "ex-（摊在外面）→ 明确的、直截了当的", "ex"),
            new("implicit", "im-（折在里面）→ 含蓄的、隐含的", "im")
        ]),
        new("form（正式）与 former（以前）", [
            new("formally", "formal（正式的）+ -ly → 正式地", "a"),
            new("formerly", "former（从前的）+ -ly → 以前、从前", "er")
        ]),
        new("i（天才 genius）与 u（天然质朴）", [
            new("ingenious", "i 联想 genius（天才）→ 巧妙的、有独创性的", "i"),
            new("ingenuous", "u 像开口坦诚微笑 → 天真的、坦率的", "u")
        ]),
        new("单 t（更晚）与双 tt（后者）", [
            new("later", "单 t（late 比较级）→ 后来、更晚", "t"),
            new("latter", "双 tt（两者排在第二个）→ 后者、后半的", "tt"),
            new("lately", "-ly 副词 → 最近、近来", "ly")
        ]),
        new("med / met / ment 辨形 · 奖牌、干涉、金属、精神", [
            new("medal", "d 像圆形奖章 → 奖牌、勋章", "d"),
            new("meddle", "双 dd（多管闲事伸两只手）→ 干涉、乱动", "ddle"),
            new("metal", "t 像坚硬钢架 → 金属", "t"),
            new("mental", "ment（心智、头脑）→ 精神的、心理的", "nt")
        ]),
        new("pa 与 pe 词首 · 巡逻 vs 汽油", [
            new("patrol", "pa 记作「爬坡巡逻」→ 巡逻、巡查", "a"),
            new("petrol", "pe 来自 petroleum（石油）→ 汽油", "e")
        ]),
        new("a（walk 漫步）与 o（张嘴惊叹）", [
            new("wander", "a 联想 walk（漫步）→ 漫游、徘徊、走神", "a"),
            new("wonder", "o 像张大嘴惊奇 → 想知道、惊叹、奇迹", "o")
        ]),
        new("wea-（空气）与 whe-（疑问）", [
            new("weather", "含 air 的 a（大气变化）→ 天气、经受住", "ea"),
            new("whether", "wh- 疑问引导词 → 是否", "he")
        ]),
        new("voc（召唤）与 vac（空出）", [
            new("vocation", "voc（声音召唤 = 天职）→ 职业、使命", "o"),
            new("vacation", "vac（空出时间）→ 假期、休假", "a")
        ]),
        new("无 e（城市）与有 e（优雅礼节）", [
            new("urban", "词根 urb（城市）→ 城市的、都市的", "an"),
            new("urbane", "多一个 e（都市绅士风度）→ 温文尔雅的、有礼貌的", "ane")
        ]),
        new("war（警惕）与 wear（磨累）", [
            new("wary", "war（战时保持戒备）→ 谨慎的、警惕的", "a"),
            new("weary", "wear（磨损耗尽）→ 疲倦的、厌烦的", "ea")
        ]),
        new("ac 与 ca 字母顺序 · 神圣 vs 害怕", [
            new("sacred", "sacr（神圣，同 sacrifice）→ 神圣的、庄严的", "ac"),
            new("scared", "scare（惊吓）+ -d → 害怕的、恐惧的", "ca")
        ]),
        new("ck 与 ct 词尾 · 跑道踪迹 vs 大片土地", [
            new("track", "ck 联想脚印跑道 → 轨道、足迹、追踪", "ck"),
            new("tract", "tract（拉出一整片）→ 大片土地、小册子、消化道", "ct")
        ]),
        new("ai 与 ia 顺序 · 拖尾小径 vs 试验审判", [
            new("trail", "tail（尾巴）加 r → 拖着尾巴走出的小径、痕迹、追踪", "ai"),
            new("trial", "try（尝试、审理）的名词 → 试验、审判", "ia")
        ]),
        new("tread / thread / threat · 踩踏、穿线、威胁", [
            new("tread", "最短，双脚踩踏 → 踩、踏、步态", "d"),
            new("thread", "多 h 像穿针引线 → 线、线索、帖子串", "h"),
            new("threat", "含 eat（仿佛要吃掉你）→ 威胁、恐吓", "at")
        ]),
        new("同音三兄弟 · 引用、地点、视野", [
            new("cite", "c 像张嘴引用别人话 → 引用、举证", "c"),
            new("site", "sit（坐落之地）→ 地点、遗址、网站", "s"),
            new("sight", "see（看）的名词 → 视力、景象、看见", "gh")
        ]),
        new("ac / as / ex + cess/sess · 通道、评估、过度", [
            new("access", "ac-（去接近）+ cess（走）→ 通道、使用权、获取", "ac"),
            new("assess", "as- + sess（坐下来估价）→ 评估、评定", "as"),
            new("excess", "ex-（超出）+ cess（走）→ 过度、过量、超过", "ex")
        ]),
        new("ad-（迎面顶着）与 a-（转身避开）+ verse", [
            new("adverse", "ad-（迎面冲撞）→ 不利的、逆境的", "ad"),
            new("averse", "a-（转脸离开）→ 反对的、厌恶的", "a")
        ]),
        new("共同词根 lus（玩弄）· 暗指、幻觉、妄想", [
            new("allusion", "al-（暗中指向）→ 暗指、典故", "al"),
            new("illusion", "il-（眼前错觉）→ 幻觉、错觉", "il"),
            new("delusion", "de-（严重蒙蔽）→ 妄想、错觉", "de")
        ]),
        new("censor / censure / sensor · 审查、谴责、传感器", [
            new("censor", "-or（审查员）→ 审查、审查官", "or"),
            new("censure", "-ure（严厉指责）→ 谴责、责难", "ure"),
            new("sensor", "sense（感知）+ -or → 传感器", "s")
        ]),
        new("comprehensive（全面）与 comprehensible（可理解）", [
            new("comprehensive", "-ive（全包进来）→ 全面的、综合的", "ive"),
            new("comprehensible", "-ible（能够被理解）→ 可理解的、易懂的", "ible")
        ]),
        new("cred（相信）三兄弟 · 可信、可赞、轻信", [
            new("credible", "-ible（能够相信）→ 可信的、可靠的", "ible"),
            new("creditable", "credit（赞誉）+ -able → 值得称赞的", "itable"),
            new("credulous", "-ulous（太容易相信）→ 轻信的", "ulous")
        ]),
        new("cease（停止）与 ease（舒适）", [
            new("deceased", "de- + cease（生命停止）→ 已故的、亡故的", "ce"),
            new("diseased", "dis-（不）+ ease（舒适）→ 患病的、病态的", "se")
        ]),
        new("ee 相连（嘴严）与 e-t-e 隔开（分离）", [
            new("discreet", "双 ee 紧紧挨着，守口如瓶 → 谨慎的、审慎的", "ee"),
            new("discrete", "两个 e 被 t 隔开 → 离散的、分离的、不连续的", "ete")
        ]),
        new("a（双并列）与 e（举剑决斗）", [
            new("dual", "du（二）+ -al → 双重的、两部分的", "a"),
            new("duel", "e 联想剑刃交锋 → 决斗、激烈竞争", "e")
        ]),
        new("marit（婚姻）与 mart（战神）", [
            new("marital", "marit 联想 marry（结婚）→ 婚姻的、夫妻的", "it"),
            new("martial", "mart 来自战神 Mars → 军事的、武术的（martial arts）", "ti")
        ]),
        new("con- + tend / tent / text / test · 抓后半词根", [
            new("contend", "con- + tend（伸手争夺）→ 竞争、主张、争辩", "nd"),
            new("content", "con- + tent（装在里面）→ 内容；满足的", "nt"),
            new("context", "con- + text（织在一起的文本）→ 上下文、语境、背景", "xt"),
            new("contest", "con- + test（一起检验较量）→ 比赛、争夺、质疑", "st")
        ]),
        new("共同词根 spect（看）· 记前缀方向一目了然", [
            new("expect", "ex-（向外张望）+ spect → 期待、预料", "ex"),
            new("inspect", "in-（向里细看）+ spect → 检查、视察", "in"),
            new("respect", "re-（再三回头看）+ spect → 尊重、方面", "re"),
            new("suspect", "sus-（从下暗中看）+ spect → 怀疑、嫌疑人", "su"),
            new("aspect", "a-（朝向一面看）+ spect → 方面、外观", "a"),
            new("prospect", "pro-（向前远看）+ spect → 前景、展望", "pro")
        ]),
        new("共同词根 pos（放置）· 记前缀方向一目了然", [
            new("compose", "com-（放到一起）+ pose → 组成、创作、使平静", "com"),
            new("expose", "ex-（放到外面）+ pose → 暴露、揭露、接触", "ex"),
            new("impose", "im-（强压在上面）+ pose → 强加、征税", "im"),
            new("oppose", "op-（放到对面）+ pose → 反对、对抗", "op"),
            new("propose", "pro-（放到前面）+ pose → 提议、求婚", "pro"),
            new("dispose", "dis-（分开放掉）+ pose → 处理、安排", "dis"),
            new("suppose", "sup-（放在下面托底）+ pose → 假设、料想", "sup")
        ]),
        new("共同词根 tain（拿住、保持）· 记前缀方向一目了然", [
            new("contain", "con-（全部装住）+ tain → 包含、容纳、克制", "con"),
            new("detain", "de-（扣留下来）+ tain → 拘留、耽搁", "de"),
            new("maintain", "main-（用手托住）+ tain → 维持、维修、主张", "main"),
            new("obtain", "ob-（拿到手里）+ tain → 获得、得到", "ob"),
            new("retain", "re-（留下来不放）+ tain → 保留、保持、记住", "re"),
            new("sustain", "sus-（从下面撑住）+ tain → 支撑、维持、经受", "sus")
        ]),
        new("共同词根 ceive（拿、抓）· 记前缀一眼分清", [
            new("conceive", "con-（脑中抓到想法）→ 构想、设想、怀孕", "con"),
            new("deceive", "de-（往下拐骗）→ 欺骗、蒙蔽", "de"),
            new("perceive", "per-（彻底看穿抓住）→ 察觉、感知、理解", "per"),
            new("receive", "re-（收回到手里）→ 收到、接待", "re")
        ]),
        new("共同词根 mit（送、放）· 记前缀方向一目了然", [
            new("admit", "ad-（放进来）→ 承认、准许进入", "ad"),
            new("commit", "com-（全心交付）→ 承诺、投入、犯（错）", "com"),
            new("emit", "e-（送出去）→ 排放、发出（光/热）", "e"),
            new("omit", "o-（放掉省去）→ 省略、遗漏", "o"),
            new("permit", "per-（一路放行）→ 允许、许可证", "per"),
            new("submit", "sub-（放低交上）→ 提交、顺从", "sub"),
            new("transmit", "trans-（跨过去送）→ 传输、传播", "trans")
        ]),
        new("共同词根 tract（拉）· 记前缀方向一目了然", [
            new("attract", "at-（拉过来）→ 吸引", "at"),
            new("contract", "con-（拉到一起）→ 收缩；双方拉手签合同", "con"),
            new("distract", "dis-（把注意力拉散）→ 使分心", "dis"),
            new("extract", "ex-（拉出来）→ 提取、摘录", "ex"),
            new("subtract", "sub-（往下拉走一部分）→ 减去、扣除", "sub")
        ]),
        new("共同词根 press（压）· 记前缀方向一目了然", [
            new("compress", "com-（往一起压）→ 压缩", "com"),
            new("depress", "de-（往下压）→ 使沮丧、使萧条", "de"),
            new("express", "ex-（从心里压出来）→ 表达；特快", "ex"),
            new("impress", "im-（压进心里）→ 给…留下深刻印象", "im"),
            new("oppress", "op-（迎面重压）→ 压迫、压制", "op"),
            new("suppress", "sup-（压在下面不让冒头）→ 镇压、抑制", "sup")
        ]),
        new("共同词根 duce（引导）· 记前缀方向一目了然", [
            new("deduce", "de-（向下推出结论）→ 演绎、推断", "de"),
            new("induce", "in-（往里引导）→ 诱导、引起", "in"),
            new("introduce", "intro-（带进来）→ 介绍、引入", "intro"),
            new("produce", "pro-（往前引出成果）→ 生产、产生", "pro"),
            new("reduce", "re-（往回拉少）→ 减少、降低", "re"),
            new("seduce", "se-（拉偏走歪）→ 诱惑、勾引", "se")
        ]),
        new("共同词根 ject（投掷）· 记前缀方向一目了然", [
            new("reject", "re-（扔回去）→ 拒绝、驳回", "re"),
            new("inject", "in-（射进去）→ 注射、注入", "in"),
            new("object", "ob-（迎面顶回去）→ 反对；摆在眼前的物体", "ob"),
            new("project", "pro-（往前投出去）→ 投射、计划项目", "pro"),
            new("subject", "sub-（扔在下面管束）→ 主题、学科；使服从", "sub"),
            new("eject", "e-（弹出去）→ 弹出、驱逐", "e")
        ]),
        new("共同词根 vert（转）· 记前缀方向一目了然", [
            new("convert", "con-（彻底转变）→ 转换、改变信仰", "con"),
            new("divert", "di-（转开岔路）→ 转移、使转向、娱乐", "di"),
            new("invert", "in-（翻转过来）→ 倒置、使颠倒", "in"),
            new("revert", "re-（转回原样）→ 恢复、重提", "re"),
            new("avert", "a-（转向避开）→ 避免、转移（目光）", "a")
        ]),
        new("共同词根 sume（拿、取）· 记前缀一目了然", [
            new("consume", "con-（全部拿光）→ 消耗、消费", "con"),
            new("assume", "as-（先拿来当真）→ 假设、承担", "as"),
            new("presume", "pre-（提前拿定）→ 假定、推定", "pre"),
            new("resume", "re-（重新拿起来）→ 重新开始、恢复；简历", "re")
        ]),
        new("共同词根 scribe（写）· 记前缀一目了然", [
            new("describe", "de-（写下来）→ 描写、形容", "de"),
            new("inscribe", "in-（刻写进去）→ 铭刻、题写", "in"),
            new("prescribe", "pre-（提前写好）→ 开处方、规定", "pre"),
            new("subscribe", "sub-（在下面签名）→ 订阅、赞同", "sub"),
            new("ascribe", "a-（写到某人名下）→ 归因于", "a")
        ]),
        new("共同词根 serve（守、留）· 记前缀一目了然", [
            new("preserve", "pre-（提前守好）→ 保存、保护（免遭破坏）", "pre"),
            new("reserve", "re-（留着备用）→ 预订、保留、储备", "re"),
            new("deserve", "de-（凭付出应得）→ 值得、应受", "de"),
            new("observe", "ob-（盯着守望）→ 观察、遵守、庆祝", "ob"),
            new("conserve", "con-（共同守住不浪费）→ 节约、保护（资源）", "con")
        ])
    ];

    private static readonly Dictionary<string, ConfusionGroup> ByWord = BuildIndex();

    private static Dictionary<string, ConfusionGroup> BuildIndex()
    {
        var map = new Dictionary<string, ConfusionGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in Groups)
        {
            foreach (var m in g.Members)
                map.TryAdd(m.Word, g);
        }
        return map;
    }

    public static ConfusionGroup? FindGroup(string word) =>
        string.IsNullOrWhiteSpace(word) ? null : ByWord.GetValueOrDefault(word.Trim());

    public static ConfusionWordHook? FindHook(string word)
    {
        var g = FindGroup(word);
        return g?.Members.FirstOrDefault(m => m.Word.Equals(word.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> GetSiblings(string word)
    {
        var g = FindGroup(word);
        if (g == null) return [];
        return g.Members
            .Where(m => !m.Word.Equals(word.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Word)
            .ToArray();
    }
}
