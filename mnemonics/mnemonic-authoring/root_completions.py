"""Reviewed whole-word memory bridges; keep unfinished anchor notes visibly separate in the audit."""

ROOT_GLOSSES = {
    'advise, warn':'提醒、警告', 'alone':'单独的', 'arms, weapons':'武器', 'ask':'询问、要求',
    'balance, weight':'天平、重量', 'ball':'球', 'beat':'打、击', 'beginning':'开始', 'best':'最好的',
    'blow, puff':'吹、鼓起', 'book':'书', 'calm; please':'使平静、使满意', 'center':'中心',
    'change, interchange':'改变、交换', 'child':'儿童', 'climb':'攀登', 'clothe; clothing':'穿衣、衣服',
    'color, pigment; the element chromium or the substance chromatin':'颜色、色素', 'confusion':'搅动、混乱',
    'conquer':'征服、战胜', 'copy, imitate':'模仿', 'death':'死亡', 'declare':'宣布、说出',
    'defense against':'防御、抵挡', 'divide':'分开', 'error, wander':'走偏、出错', 'excessive desire':'狂热',
    'faith, trust':'信任', 'father, fatherland':'父亲、祖国', 'feeling, sensation, sense perception':'感觉、感知',
    'female, woman':'女性', 'field, farm, crop':'田地、农业', 'fit for':'适合', 'flee':'逃离',
    'flock, gathering':'群、聚集', 'free':'自由的', 'from, away from, off, off from':'离开、脱离',
    'give an order':'命令', 'god':'神', 'grant; allot':'给予、分配', 'heap':'堆', 'heat, temperature':'热、温度',
    'heavy':'重的', 'highest':'最高', 'holy':'神圣的', 'host, guest':'主人、客人', 'house':'家',
    'human being':'人', 'image':'形象', 'joint':'关节', 'language, tongue':'语言、舌头', 'largest':'最大的',
    'laugh':'笑', 'leaf, page':'叶、页', 'like, similar':'相似的', 'likeness':'形象、相似',
    'luck, chance':'运气、机会', 'machine; means':'机械、工具', 'make firm; support':'使坚固、支持',
    'mark':'记号', 'medical care, healing':'医疗、治疗', 'more':'更多', 'mother':'母亲', 'nerve':'神经',
    'no':'不', 'on':'在……上', 'opinion, belief':'意见、信念', 'pain':'痛',
    'particular or personal to; distinct':'个人的、独有的', 'peace':'和平、约定', 'perform':'执行、履行',
    'pleasing':'使愉快、使满意', 'pressure, weight':'压力、重量', 'ray, spoke':'光线、辐条',
    'resembling, like':'像……的', 'rub':'摩擦', 'save, keep':'保存、保留', 'scrape':'刮、擦',
    'seize, carry off by force':'强夺', 'serve, assist':'服务、协助', 'shine':'照亮', 'ship':'船',
    'ship; sailor':'船、航行者', 'side':'边、侧面', 'slave':'奴仆、服侍', 'slip, fall back':'滑落、流逝',
    'son or daughter':'子女', 'spiral, circular':'螺旋的', 'stiff, rigid':'僵硬的',
    'straight, vertical, perpendicular; correct':'直的、正确的', 'stranger, enemy':'陌生人、敌人',
    'strike':'打、击', 'teach':'教、使知道', 'the people':'人民', 'thread, line':'线', 'truth':'真实',
    'twist':'扭转', 'wander':'游走、偏离', 'war':'战争', 'weave':'编织', 'wheel, circle':'轮、圆圈',
    'wing, fin':'翅膀、鳍', 'wise, wisdom':'智慧', 'wish, intention, will':'意愿',
    'writing, drawing, recording':'书写、绘画、记录',
}

def localize_glosses(text):
    for english, chinese in ROOT_GLOSSES.items():
        text=text.replace('（'+english+'）','（'+chinese+'）')
    return text

_ROWS = r"""
pronounce|词根词缀|pro-（向外、公开）＋nounce（宣布、说出）→把声音说出来→发音；向公众正式说出来→宣告。nounce-/nunci- 都与“宣布、说出”有关：announce 宣布，denounce 公开谴责，pronunciation 发音。
pronunciation|词根词缀|pro-（向外）＋nunci（宣布、说出）＋-ation（行为、方式）→把语音说出来的方式→发音、读法。pronounce 中的 nounc 到这里变成 nunci，拼写是 pronunciation，中间没有 ou。
announce|词根词缀|an-（向，ad- 在 n 前的同化形式）＋nounce（宣布、说出）→向大家传达消息→宣布、公布。announcement：announce 去掉末尾 e，接 -ment（行为或结果）→公告、声明。
announcement|词根词缀|announce（宣布）去掉末尾 e，接 -ment（行为或结果）→宣布这一行为，或宣布出来的消息→宣布、公告、声明。announce＝an-（向）＋nounce（说出）。
denounce|词根词缀|de-（向下）＋nounce（宣布、说出）→公开指出并贬斥某人的恶行→谴责、告发。pronounce 是“说出”，announce 是“向大家宣布”，denounce 带公开指责的意思。
denunciation|词根词缀|de-（向下）＋nunci（宣布、说出）＋-ation（行为）→公开说出并指责坏事→谴责、告发。denounce 的 nounc 在这个名词中变为 nunci。
renounce|词根词缀|re-（回、反向）＋nounce（宣布、说出）→把先前接受的事物公开退回去→声明放弃、宣布断绝。放弃权利、信仰或关系，都带明确表态的意味。
enunciate|词根词缀|e-（向外，ex- 的变体）＋nunci（说出）＋-ate（动词词尾）→把话清清楚楚说出来→清晰发音、阐明。
provide|词根词缀|pro-（提前）＋vide（看，vid-/vis- 都有“看”的意思）→提前看见需求，事先备好所需物品→提供、供应；为将来作准备。
provision|词根词缀|pro-（提前）＋vis（看）＋-ion（行为、结果）→预先看见需要并备好东西→准备、供应；备好的东西→供应品、粮食；事先订下的安排→条款。
include|词根词缀|in-（向内）＋clude（关闭）→关进同一个范围里→包括、包含。exclude：ex-（向外）＋clude→把某物关在范围之外→排除。
including|词根词缀|include（包括）去掉末尾 e，接 -ing→包括……在内。include＝in-（向内）＋clude（关闭）：把列举的人或事物也关进同一范围。
exclude|词根词缀|ex-（向外）＋clude（关闭）→把某物关在外面→排除、不包括。include 的 in- 向内，exclude 的 ex- 向外，方向相反。
conclude|词根词缀|con-（完全、一起）＋clude（关闭）→把事情收拢、关合→结束；把各条证据收拢后定下来→下结论。conclusion＝con-＋clus（关闭）＋-ion（结果）→结论、结束。
preclude|词根词缀|pre-（预先）＋clude（关闭）→事先把通道关上→使不可能、阻止。不是事情发生后处理，而是提前堵住发生的可能。
require|词根词缀|re-（再、加强）＋quire（寻找、寻求）→不断寻求必须得到的东西→需要、要求。inquire 向里面追问，acquire 寻求并取得。
president|词根词缀|pre-（在前）＋sid（坐）＋-ent（做某事的人）→坐在前面主持事务的人→主席、总裁；国家的主持者→总统。
process|词根词缀|pro-（向前）＋cess（走）→一步步向前走→过程、进程、程序。把原料按步骤向前处理→加工、处理。proceed 也含 pro-＋ceed（走），表示继续进行。
procedure|词根词缀|pro-（向前）＋ced（走）＋-ure（行为、方式）→事情向前进行所遵循的方式→程序、手续、步骤。
produce|词根词缀|pro-（向前、向外）＋duce（引导、带出）→把成果带出来→生产、制造；把证据拿出来→出示。作名词时常指农产品。
product|词根词缀|pro-（向前、向外）＋duct（引导、带出）→生产活动带出来的东西→产品、产物；某种原因带来的东西→结果。
reduce|词根词缀|re-（回）＋duce（引导、带回）→把数量或程度带回较低水平→减少、降低；把物质带回较原始的状态→还原。
introduce|词根词缀|intro-（向内）＋duce（引导、带入）→把人或事物带进来→引入；把陌生人带入彼此认识的范围→介绍；把话题带进谈话→提出。
involve|词根词缀|in-（向内）＋volve（卷、转）→卷进去→使卷入、牵涉；把若干部分卷在一件事中→包含、涉及。
revolve|词根词缀|re-（回、反复）＋volve（卷、转）→转一圈又回来→旋转、围绕……转。revolution：re-＋volut（转）＋-ion（过程）→旋转一周；局面彻底转变→革命。
evidence|词根词缀|e-（向外）＋vid（看）＋-ence（状态、事物）→显露在外、能够看见的依据→证据、迹象。evident：e-＋vid＋-ent（形容词词尾）→显而易见的。
evident|词根词缀|e-（向外）＋vid（看）＋-ent（形容词词尾）→显露在外，能直接看见→明显的、显然的。
encourage|词根词缀|en-（使具有）＋courage（勇气，cour 与“心”有关）→把勇气放进心里→鼓励、支持。discourage：dis-（除去）＋courage→使失去勇气→使气馁。
ensure|词根词缀|en-（使成为）＋sure（确定的）→使事情确定下来→确保、保证。看见 en- 接熟悉的形容词，想到“使具有这种状态”。
enable|词根词缀|en-（使成为）＋able（有能力的）→使有能力做某事→使能够、使成为可能；给设备增加能力→启用。
enjoy|词根词缀|en-（使处于某种状态）＋joy（欢乐）→置身欢乐之中→享受、喜爱。快乐来自正在经历的事情，enjoy 后接名词或 doing。
attention|词根词缀|at-（向，ad- 的同化形式）＋tent（伸、拉）＋-ion（行为、状态）→把心神伸向某事→注意、注意力。attend＝at-＋tend（伸向）→留心、照料；到场参加。
attend|词根词缀|at-（向）＋tend（伸、拉）→把心神伸向某事→注意、照料；把自己带到某个场合、在那里投入注意→出席、参加。
intend|词根词缀|in-（向、朝着）＋tend（伸、拉）→把心意伸向一个目标→打算、意图。intention＝in-＋tent（伸）＋-ion（行为、状态）→意图、目的。
extend|词根词缀|ex-（向外）＋tend（伸、拉）→向外拉开→延伸、扩大；把时间向后拉长→延长；把帮助伸向别人→给予、提供。
extent|词根词缀|ex-（向外）＋tent（伸、拉）→向外伸展到的边界→范围、程度。问 to what extent，就是问伸展到了什么程度。
prevent|词根词缀|pre-（提前）＋vent（来）→在坏事来到以前先采取行动→预防、防止。vent-/ven- 有“来”的意思：event 来到眼前的事，intervene 进入两者之间。
refer|词根词缀|re-（回）＋fer（带、拿）→把注意带回某个人或材料→提到、参考；把问题转交回相应的人→提交、转交。
relate|词根词缀|re-（回）＋late（带、搬运，fer 的词根变体）→把消息带回来并说给别人听→讲述；把两件事带到一起比较→联系、有关联。
protect|词根词缀|pro-（在前）＋tect（覆盖）→挡在前面覆盖住→保护、防护。detect＝de-（去掉）＋tect（覆盖）→去掉遮盖，看出真相→发现、查明。
detect|词根词缀|de-（去掉）＋tect（覆盖）→揭去遮盖→发现、察觉、查明。protect 在前面遮护，detect 则揭开遮盖。
determine|词根词缀|de-（加强、完全）＋termine（界限，term 有“终点、边界”的意思）→给事情划定界限、定下来→决定、确定。determination：决定后不再摇摆→决心。
define|词根词缀|de-（完全、明确）＋fine（界限，fin- 有“终点、边界”的意思）→把概念的边界划清→定义、规定。把清晰的界线画出来，也可指使轮廓分明。
finish|词根词缀|fin（终点、界限）＋-ish（动词词尾）→走到终点→完成、结束。用在表面处理上，是把最后一道工序做完→抛光、饰面。
final|词根词缀|fin（终点）＋-al（与……有关的）→在终点的→最后的、最终的；放在学期终点的考试→期末考试；比赛最后一轮→决赛。
recognize|词根词缀|re-（再）＋cogn（知道、认识）＋-ize（动词词尾）→再次认识到、认出熟悉的人或事→认出、识别；确认其身份或价值→承认、认可。
indicate|词根词缀|in-（向、朝着）＋dic（说、指出）＋-ate（动词词尾）→朝某个方向指出来→指示、表明；迹象把结论指出来→象征、意味着。
benefit|词根词缀|bene（好）＋fit（做，fac-/fect- 的变体）→做出好处→使受益；做成的好事、得到的好处→利益、益处。
compare|词根词缀|com-（一起）＋pare（相等，par- 有“相等”的意思）→放到一起衡量是否相等→比较、对照；把两者看作相似→比作。
complete|词根词缀|com-（完全）＋plete（装满，ple-/plet- 有“满”的意思）→完全填满，不缺任何部分→完整的、完全的；把剩下部分填齐→完成。
perfect|词根词缀|per-（完全、彻底）＋fect（做）→彻底做成→完美的、完善的；把事情做得完善→使完美。语法上的完成时，也与动作已经完成有关。
depend|词根词缀|de-（向下）＋pend（悬挂）→挂在某物下面，需要它支撑→依赖、依靠；结果悬在条件上→取决于。
suspend|词根词缀|sus-（从下面、向上托）＋pend（悬挂）→托起后悬在那里→悬挂；事情悬着不继续→暂停、中止。
context|词根词缀|con-（一起）＋text（编织）→把前后的话编织在一起→上下文；把一件事周围的情况织在一起→背景、语境。
text|词源联想|text 与拉丁语“编织”有关：把词句像线一样编织成篇→文本、正文。context 是 con-（一起）＋text（编织）→前后织在一起的上下文；textile 指纺织品。
pretext|词根词缀|pre-（在前）＋text（编织）→事先编好、摆在表面的说法→借口、托词。真实原因藏在后面，前面盖着一层编好的话。
textile|词根词缀|text（编织）＋-ile（与……有关的）→编织成的东西→纺织品；与编织有关的→纺织的。
conserve|词根词缀|con-（完全、一起）＋serve（保存）→把已有东西完整保存下来→保存、保护；节省资源，让它不被消耗掉。
preserve|词根词缀|pre-（提前）＋serve（保存、守护）→提前保护，免遭损坏→保存、保护；把食物提前处理以防腐坏→腌制、保存食品。
reserve|词根词缀|re-（回、留在后面）＋serve（保存）→留下一部分保存着→保留、储备；提前留住座位或房间→预订。
confirm|词根词缀|con-（完全、加强）＋firm（牢固）→让原先的判断变牢靠→证实、确认；使安排或关系固定下来→确定、巩固。
affirm|词根词缀|af-（加强，ad- 的同化形式）＋firm（牢固）→坚定地说某事属实→断言、肯定；坚定表示支持→维护。
infirm|词根词缀|in-（不）＋firm（牢固、强健）→不强健的→虚弱的、体弱的；缺乏坚定支撑→不坚固的。
contribute|词根词缀|con-（一起）＋tribute（给予、分配）→每个人一起拿出一份→贡献、捐助；给共同的文章拿出自己的一份→投稿；给结果增加一份作用→促成。
attribute|词根词缀|at-（向）＋tribute（给予、分配）→把某种结果或性质分给某人→归因于、归属于；分配给事物的特点→属性、特征。
tribute|词根词缀|tribut- 有“分配、缴纳”的意思。把一份财物呈送出去→贡品、贡金；把一份敬意献出去→致敬、颂辞。
confide|词根词缀|con-（完全）＋fide（信任，fid- 有“信”的意思）→完全信任一个人，把心里的事交给他→吐露、倾诉；信赖。
confident|词根词缀|con-（完全）＋fid（信任）＋-ent（形容词词尾）→充分相信自己或某个结果→自信的、确信的。
convince|词根词缀|con-（完全）＋vince（征服、战胜）→用理由战胜疑虑→说服、使确信。victory 的 vict 也与“战胜”有关。
convinced|词根词缀|convince（说服、使确信）去掉末尾 e，接 -ed→被理由说服的→确信的、坚信的。convince＝con-（完全）＋vince（战胜）。
convict|词根词缀|con-（完全）＋vict（战胜）→证据压倒辩解→证明有罪、定罪；被判有罪的人→已决犯、囚犯。
victory|词根词缀|vict（战胜）＋-ory（名词词尾）→战胜对手这一结果→胜利。convince 用理由战胜疑虑，victory 则是比赛或斗争中的胜利。
ascend|词根词缀|a-（向，ad- 的变体）＋scend（攀登）→向上攀登→上升、登上。descend 的 de- 表“向下”，两者方向相反。
descend|词根词缀|de-（向下）＋scend（攀登、移动）→沿着高处往下走→下降、下来；家族一代代往下传→出身于、传下来。
descent|词根词缀|de-（向下）＋scent（攀登、移动，scend 的变体）→向下移动的过程→下降；家族从祖先一代代传下来→血统、出身。
transcend|词根词缀|trans-（越过）＋scend（攀登）→攀过原有的界限→超越、超过。不是停在边界内，而是继续向上、跨出去。
transcendence|词根词缀|transcend（超越）＋-ence（状态）→超出原有界限的状态→超越、超然。trans-（越过）＋scend（攀登），再把动作转成状态。
concentrate|词根词缀|con-（一起）＋centr（中心）＋-ate（动词词尾）→聚到同一个中心→集中；把溶液中的有效成分聚得更密→浓缩。
eccentric|词根词缀|ec-（向外）＋centr（中心）＋-ic（形容词词尾）→偏离中心的→偏心的；人的行为偏离常规→古怪的、异乎寻常的。
geocentric|词根词缀|geo（地球）＋centr（中心）＋-ic（形容词词尾）→以地球为中心的→地心说的、地心的。
aggregate|词根词缀|ag-（向，ad- 的同化形式）＋greg（群）＋-ate（动词或形容词词尾）→聚成一群、一整体→聚集、合计；总的、合计的。
congregate|词根词缀|con-（一起）＋greg（群）＋-ate（动词词尾）→聚在一起成群→集合、聚集。
segregate|词根词缀|se-（分开）＋greg（群）＋-ate（动词词尾）→从同一群中分开→隔离、使分离；按身份把人群分开→实行隔离。
inflate|词根词缀|in-（向内）＋flate（吹，flat-/fl- 有“吹”的意思）→往里面吹气→使充气、膨胀；把价格或数字吹大→使上涨、夸大。
aggravate|词根词缀|ag-（加强，ad- 的同化形式）＋grav（重）＋-ate（动词词尾）→使问题更重→加重、恶化；把人的烦恼加重→激怒。
liberate|词根词缀|liber（自由）＋-ate（动词词尾）→使自由→解放、释放。把人从束缚中放出来，也把气体从容器中释放出来。
liberal|词根词缀|liber（自由）＋-al（与……有关的）→思想不被狭窄限制束缚→开明的、自由主义的；给东西不受小气束缚→慷慨的。
liberty|词根词缀|liber（自由）＋-ty（性质、状态）→自由的状态→自由、自主权；不受约束地做某事的权利。
domestic|词源联想|dom- 有“家”的意思。属于家里的→家庭的、家务的；把国家当作自己的家→国内的；动物生活在人家中→家养的。
domestically|词根词缀|domestic（家庭的、国内的）＋-ally（副词词尾）→在家庭内部、在国内。dom- 是“家”：由一个家庭的内部推到一个国家的内部。
solitary|词根词缀|solit（单独，与 sol- 有关）＋-ary（形容词词尾）→独自一人的→独居的、孤独的；数量上只有一个→单独的。
solitude|词根词缀|sol（单独）＋-itude（状态）→独自一人的状态→独处、孤独。solitary 描写独自的，solitude 是这种状态。
solo|词源联想|solo 来自“单独的”：舞台上只有一个人演奏→独奏、独唱；只由一个人完成→单独的、单独地。
paternal|词根词缀|patern（父亲）＋-al（与……有关的）→父亲的、父系的；像父亲那样关怀→慈父般的。maternal 的 matern 是“母亲”。
paternity|词根词缀|patern（父亲）＋-ity（身份、状态）→父亲的身份→父亲身份、父子关系。paternity test 是确认父子关系的鉴定。
maternal|词根词缀|matern（母亲）＋-al（与……有关的）→母亲的、母系的；像母亲那样关怀→母亲般的。
maternity|词根词缀|matern（母亲）＋-ity（身份、状态）→成为母亲的身份或状态→母亲身份、孕产；与生育有关的→产科的。
feminine|词根词缀|femin（女性）＋-ine（与……有关的）→女性的、女性特有的；语法中与阴性类别有关→阴性的。
feminism|词根词缀|femin（女性）＋-ism（主张、思想）→主张女性平等权利的思想→女权主义、女性主义。
feminist|词根词缀|femin（女性）＋-ist（某种主张的支持者）→支持女性平等权利的人→女权主义者；女性主义的。
neurology|词根词缀|neur（神经）＋-ology（学科）→研究神经系统的学科→神经学、神经病学。
neurologist|词根词缀|neuro（神经）＋log（研究）＋-ist（从事某事的人）→研究、诊治神经系统的人→神经科医生、神经学家。
neurological|词根词缀|neuro（神经）＋log（研究）＋-ical（与……有关的）→与神经系统及神经学有关的→神经学的、神经系统的。
arthritis|词根词缀|arthr（关节）＋-itis（炎症）→发生在关节的炎症→关节炎。-itis 见于多种炎症名称，识别它就能想到“炎”。
barometer|词根词缀|baro（压力、重量）＋meter（测量仪）→测量气压的仪器→气压计；能反映局势变化的指标→晴雨表。
thermostat|词根词缀|thermo（热、温度）＋stat（保持稳定）→让温度保持稳定的装置→恒温器、温控器。
astronaut|词根词缀|astro（星、太空）＋naut（航行者）→在星际、太空中航行的人→宇航员。nautical 的 naut 也是航行，astronaut 把海上航行延伸到太空。
nautical|词根词缀|naut（船、航行）＋-ical（与……有关的）→航海的、船舶的。
naval|词根词缀|nav（船）＋-al（与……有关的）→船舶的；与国家舰船部队有关→海军的。
navigate|词根词缀|nav（船）＋ig（驱动、引导，ag- 的变体）＋-ate（动词词尾）→引导船只前进→航行、导航；为人指引路线→引路。
bilateral|词根词缀|bi-（二）＋later（边、侧面）＋-al（与……有关的）→涉及两边的→双边的、双方的。lateral 是侧面的，multilateral 的 multi- 表“多”。
lateral|词根词缀|later（边、侧面）＋-al（与……有关的）→侧面的、横向的。从中间朝一侧移动，就是 lateral movement（横向移动）。
collapse|词根词缀|col-（一起，con- 的同化形式）＋lapse（滑落、倒下）→各部分一起倒下→倒塌、崩溃；身体支撑不住而倒下→突然虚脱。
elapse|词根词缀|e-（向外、离去）＋lapse（滑落）→时间像水一样滑走→时间流逝、过去。
lapse|词源联想|laps- 有“滑落”的意思：注意力滑开一下→疏忽、失误；状态滑落→衰退；期限走过、权利失效→期满、失效。
conflict|词根词缀|con-（一起）＋flict（打、击）→两股力量打到一起→冲突、斗争；意见彼此相撞→抵触、不一致。
inflict|词根词缀|in-（向内、加到……上）＋flict（打、击）→把打击加到别人身上→使遭受、施加。加上去的通常是痛苦、伤害或惩罚。
distort|词根词缀|dis-（分开、偏离）＋tort（扭）→扭离原来的形状→使变形；把事实扭离原貌→歪曲。
torture|词根词缀|tort（扭）＋-ure（行为、结果）→把身体扭得痛苦→拷打、折磨；让精神像被拧住一样痛苦→煎熬。
verify|词根词缀|ver（真实）＋-ify（使成为、查明）→查明某事是否真实→核实、验证。
verification|词根词缀|ver（真实）＋-ification（使……的行为、过程）→核实真实性这一过程→核实、验证。verify 是动作，verification 是过程或结果。
philosophy|词根词缀|philo（爱）＋sophy（智慧）→爱智慧、追求智慧→哲学。不是死记知识，而是追问事物的道理。
philosopher|词根词缀|philo（爱）＋soph（智慧）＋-er（人）→爱智慧、研究哲理的人→哲学家。
philosophical|词根词缀|philo（爱）＋soph（智慧）＋-ical（与……有关的）→哲学的；遇事能从道理上想开→达观的、冷静的。
voluntary|词根词缀|volunt（意愿）＋-ary（与……有关的）→出于自己意愿的→自愿的、志愿的；动作由自己的意志控制→随意的。
benevolent|词根词缀|bene（好）＋vol（意愿）＋-ent（形容词词尾）→怀着善意的→仁慈的、乐善好施的。
benevolence|词根词缀|bene（好）＋vol（意愿）＋-ence（性质、状态）→怀有善意的品性→仁慈、善意；把善意落实为帮助→善行、捐助。
agriculture|词根词缀|agri（田地）＋culture（耕作、培育）→在田地里耕作和培育→农业。culture 也可把对土地的培育引申为对人的培育→文化。
agricultural|词根词缀|agri（田地）＋cultur（耕作、培育）＋-al（与……有关的）→与农业有关的→农业的。
adapt|词根词缀|ad-（向）＋apt（适合）→向合适的状态调整→使适应、改编。把作品调整到适合另一种形式，就是改编。
apt|词根词缀|apt 有“适合”的意思：很适合某个情境→恰当的；适合做某事、学得快→聪明的；容易朝某种情况发展→有……倾向的。
aptitude|词根词缀|apt（适合）＋-itude（性质、状态）→适合做某事的内在素质→天资、才能。apt 是“适合的”，aptitude 是这种能力。
library|词根词缀|libr（书）＋-ary（存放某物的地方）→存放书籍的地方→图书馆、藏书室。
librarian|词根词缀|libr（书）＋-arian（从事相关工作的人）→负责书籍和图书馆工作的人→图书管理员。
bibliography|词根词缀|biblio（书）＋-graphy（书写、记录）→把相关书籍记录成目录→书目、参考文献。
paragraph|词根词缀|para-（在旁边）＋graph（写、记号）→原指写在旁边划分文字的记号→由它分开的段落→段落、短文。
telegraph|词根词缀|tele-（远）＋graph（写、记录）→把文字信息传到远方→电报；发电报。
illustrate|词根词缀|il-（使、加强，in- 的同化形式）＋lustr（照亮）＋-ate（动词词尾）→把问题照亮，让人看清→说明、阐明；用图画让内容更明白→加插图。
illustrious|词根词缀|il-（加强）＋lustr（照亮）＋-ious（具有……性质的）→光彩耀眼、人人看得见→杰出的、著名的。
initial|词根词缀|init（开始）＋-ial（与……有关的）→开始的、最初的；名字开头的字母→姓名首字母。
initiate|词根词缀|init（开始）＋-iate（动词词尾）→使某事开始→发起、创始；把新人带进一门学问或组织的起点→传授入门知识、接纳入会。
administer|词根词缀|ad-（向）＋minister（服务、协助）→为某项事务提供服务、把它料理好→管理、执行；向病人提供药物→给予、施用。
administrator|词根词缀|administr（管理、执行）＋-ator（做某事的人）→负责管理和执行事务的人→管理者、行政人员。administer＝ad-（向）＋minister（服务、协助）。
minister|词源联想|minist- 有“服务、协助”的意思：为国家某项事务服务的人→部长；为宗教团体服务的人→牧师；亲自提供帮助→照料、服务。
ministry|词根词缀|minist（服务、协助）＋-ry（机构、职务）→承担公共服务的机构→部、政府部门；牧师的服务职务→牧师职务、传教工作。
million|数字联想|million＝1,000,000：一千个一千，就是一百万。数字从右向左每三位分一组，million 前有两组三个零；billion 前有三组。
billion|数字联想|billion＝1,000,000,000：一千个 million（一百万），就是十亿。从右向左每三位分组，billion 前有三组三个零。
within|拆词联想|with＋in（里面）：把边界画成一个圈，盯住末尾的 in，事物落在圈里面→在内部、在范围之内。without 的末尾 out 则朝圈外。
without|拆词联想|with（带着、与……一起）＋out（外面）：把本该随身带着的东西画到圈外，身边没有它→没有、缺乏。within 盯住 in（内），without 盯住 out（外）。
service|词根词缀|serv（服侍、服务）＋-ice（名词词尾）→为别人提供帮助的活动→服务、效劳；长期提供的帮助→贡献、功劳。
accumulate|词根词缀|ac-（向、加强）＋cumul（堆）＋-ate（动词词尾）→不断往同一堆里添加→积累、积聚。钱、经验和灰尘都能一点点堆积起来。
atmosphere|词根词缀|atmo（蒸汽、气体）＋sphere（球、球层）→环绕地球的气体层→大气、大气层；场所中包围人的整体感觉→气氛、氛围。
hemisphere|词根词缀|hemi-（一半）＋sphere（球）→半个球→半球。地球分南北半球，大脑也有左右两个半球。
doctor|词根词缀|doct（教、使知道）＋-or（人）→能传授专门学问的人→博士；具有医学专门知识、为病人诊治的人→医生。
errand|拆词联想|err（出错）＋and（还有）：出去办差事，别出错，还有下一件要办→差事、跑腿。把 err 和 and 直接拼在一起，就是 errand。
victim|形近词联想|victim（受害者）与 victory（胜利）对照：一场残酷的胜利 victory，可能留下许多受害者 victim。victim 末尾是 im，victory 末尾是 ory。
special|词源联想|spec- 的“外形、样貌”发展出 species（种类）：在同类中具有自己的鲜明样貌→特别的、特殊的；为特定目的而设→专门的。
specific|词根词缀|speci（种类，来自 species）＋fic（做、造成）→造成某个种类的鲜明特征→特有的、特定的；说得能明确区分是哪一个→具体的、明确的。
species|词源联想|species 原有“外形、样貌”的意思：把外形及特征相近的生物分在一起→物种、种类。这个词的单数、复数都写 species。
individual|词根词缀|in-（不）＋divid（分开）＋-ual（形容词词尾）→不能再分成其他个体的→单独的、个体的；一个独立的人→个人、个体。
popular|词根词缀|popul（人民、大众）＋-ar（与……有关的）→面向大众、为大众喜欢的→大众的、通俗的、受欢迎的。popularity 是受欢迎的程度。
populate|词根词缀|popul（人民）＋-ate（动词词尾）→使一个地方有居民→居住于、使人口聚居；把成员填入一个集合→填充。
manage|词源联想|man- 有“手”的意思：原指用手驾驭马匹，把难控制的事物握在手中→管理、处理；有困难仍把事情控制住、做成→设法完成。
mortal|词根词缀|mort（死亡）＋-al（与……有关的）→终究会死亡的→会死的、凡人的；足以造成死亡的→致命的。immortal 的 im- 表“不”→不朽的。
mortgage|词根词缀|mort（死、终止）＋gage（抵押、担保）→债务清偿后担保即告终止的抵押契约→抵押、按揭；以房产等作担保借款→抵押贷款。
nostalgia|词根词缀|nost（回家）＋-algia（疼痛）→因想回家而心痛→乡愁、思乡；想回到过去而惆怅→怀旧。
complacent|词根词缀|com-（完全、加强）＋plac（使满意）＋-ent（形容词词尾）→对自己十分满意，甚至看不到问题→自满的、沾沾自喜的。
complacency|词根词缀|com-（完全、加强）＋plac（使满意）＋-ency（状态）→过分满意而不警惕的状态→自满、自鸣得意。
deliberate|词根词缀|de-（充分）＋liber（称量、天平）＋-ate（动词或形容词词尾）→把利弊充分称量→仔细考虑；经过考虑的→深思熟虑的；明知而为→故意的。
assimilate|词根词缀|as-（向，ad- 的同化形式）＋simil（相似）＋-ate（使成为）→使变得相似、融成一体→同化；把知识或营养变成自身的一部分→吸收、消化。
similar|词根词缀|simil（相似）＋-ar（形容词词尾）→具有相像特征的→相似的、类似的。simil- 也见于 assimilate（同化）、simulate（模拟）。
simulate|词根词缀|simul（相似）＋-ate（使成为）→做出相似的样子→模拟、模仿；故意装出某种样子→假装。
simultaneous|词根词缀|simult（同时）＋-aneous（形容词词尾）→同一时间发生的→同时发生的、同步的。把两条时间线上的事件画在同一个时间点。
doctorate|词根词缀|doctor（博士）＋-ate（身份、资格）→博士的资格、学位→博士学位。doct- 有“教、学问”的意思。
document|词源联想|doc- 有“教、使知道”的意思：能让人知道事实、作为证明的材料→文件、文献；把事实写入材料→记录、用文件证明。
bible|词源联想|bibl- 有“书”的意思。Bible 首字母大写指《圣经》；一本书若被某个领域的人奉为权威，也可称为该领域的 bible（宝典）。bibliography 是 biblio（书）＋graphy（记录）→书目。
aboard|词根词缀|a-（在……上）＋board（木板、船舷）→在船的甲板上→在船上；扩展到其他交通工具→在飞机、火车上。
ashore|词根词缀|a-（向、在）＋shore（岸）→朝岸上去、在岸上→向岸上、在岸上。船上是 aboard，上岸后是 ashore。
atop|词根词缀|a-（在……上）＋top（顶部）→在顶部→在……顶上。
"""

NOTES={}
for line in _ROWS.strip().splitlines():
    word,kind,text=line.split('|',2)
    assert word not in NOTES,word
    NOTES[word]=(kind,text)
SOURCES={word:['https://github.com/skywind3000/ECDICT/blob/master/wordroot.txt','https://github.com/skywind3000/ECDICT'] for word in NOTES}
for word in ['pronounce','pronunciation','announce','announcement','denounce','denunciation','renounce','enunciate']:
    SOURCES[word]=['https://www.etymonline.com/word/'+word]
for word in ['victim','errand']:
    SOURCES[word]=['https://github.com/skywind3000/ECDICT','原创拆词或形近词联想']
