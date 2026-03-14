# HexToPcap Windows 桌面应用实施计划

## Summary
- 技术选型固定为 `WPF + .NET Framework 4.8`，目标最低兼容 `Windows 7 SP1+`，尽量不引入第三方依赖。
- v1 做成一个本地单机工具：主窗口负责粘贴/转换，设置对话框负责输出目录和 Wireshark 路径持久化。
- 输出格式固定为经典 `pcap`，不是 `pcapng`；链路层固定按 `Ethernet (DLT_EN10MB)` 写入。
- 文件命名固定为 `yyyyMMddHHmmss-报文个数.pcap`，其中“报文个数”按成功写入的报文数计算。

## Key Changes
### 应用结构
- 建立一个 WPF 桌面项目，采用轻量 MVVM/服务分层，不引入 Prism/ReactiveUI 等框架。
- 主窗口包含：
  - 一个大号多行 `TextBox` 作为核心输入区
  - 当前输出目录展示
  - `转换并保存` 按钮
  - `设置` 按钮
  - 转换结果区域，展示成功数量、失败数量、失败原因
- 设置对话框包含两个持久化配置项：
  - `OutputDirectory`
  - `WiresharkPath`
- 配置存储使用 .NET Framework 自带用户级设置，避免额外配置文件协议设计。

### 核心接口与数据模型
- 定义 `AppSettings`：`OutputDirectory`、`WiresharkPath`
- 定义 `ParseResult`：`SuccessfulPackets`、`Errors`
- 定义 `PacketParseError`：`Index`、`Reason`、`SourcePreview`
- 定义服务边界：
  - `IInputParser`：把 textarea 文本解析为原始以太网帧列表
  - `IPcapWriter`：把报文列表写成 `.pcap`
  - `ISettingsService`：读取/保存设置
  - `IWiresharkLocator`：判断是否可用 Wireshark 打开结果文件

### 解析规则
- 支持两类输入：
  - 普通十六进制文本
  - `tcpdump -vv -nn -XX` 风格文本
- 通用预处理：
  - 统一换行
  - 保留空行作为潜在分包标记
  - 忽略行首行尾空白
- `tcpdump -XX` 解析规则：
  - 识别 `0x0000:` 这类偏移地址行
  - 只提取中间 hex 区，忽略右侧 ASCII 展示
  - 当偏移重新回到 `0x0000` 时，结束上一包并开始新包
  - 空行也可作为额外分包边界
- 普通 hex 解析规则：
  - 允许空白分隔的 hex 字节/字组
  - 空行显式分隔多个报文
  - 若无空行，则启用用户确认过的“按 Ethernet/IP 长度启发式分包”
- 启发式分包规则固定为：
  - 先按以太网头解析，支持普通 Ethernet 和多层 VLAN / QinQ 封装
  - 仅对可推断总长度的帧做自动切分：`IPv4`、`IPv6`、`ARP`
  - IPv4 依据 `Total Length`，IPv6 依据 `Payload Length + 40`，ARP 按固定长度
  - 若 EtherType 无法推断总长度，且没有空行/offset 边界，则该段记为失败，不做猜测性切分
  - 输入结束时若剩余字节不足以组成完整帧，也记为失败
- 校验规则：
  - 非 hex 字符、奇数个 hex 字符、字节数不足 14、头部长度非法，都作为失败项记录
  - 一次转换允许部分失败；只导出成功报文，并在界面列出失败项

### PCAP 输出与打开方式
- `pcap` 写入策略：
  - 经典 `libpcap` 文件头
  - `snaplen = 65535`
  - `network = 1 (Ethernet)`
  - 每条记录时间戳使用当前本地时间，后续报文按 `+1ms` 递增保证顺序稳定
- 输出目录：
  - 使用设置中的 `OutputDirectory`
  - 目录不存在时自动创建
  - 若本次无成功报文，则不生成文件，只提示错误
- Wireshark 打开策略：
  - 若设置了 `WiresharkPath` 且文件存在，转换成功后提示“是否使用 Wireshark 打开”
  - 若未配置，则查询系统 `.pcap` 打开关联
  - 只有当关联目标明确是 `wireshark.exe` 时才弹出该提示
  - 若未检测到 Wireshark，则仅提示保存成功，不额外弹框

## Test Plan
- 单元测试覆盖：
  - 单个普通 Ethernet/IPv4 报文
  - 单个报文跨多行
  - 多个普通报文以空行分隔
  - 多个普通报文无空行，依靠 IPv4 长度启发式切分
  - IPv6 报文启发式切分
  - ARP 报文启发式切分
  - 带 VLAN 的 IPv4/IPv6 报文
  - `tcpdump -XX` 单包解析
  - `tcpdump -XX` 多包连续且 offset 重置解析
  - `tcpdump -XX` 带 ASCII 尾部时正确忽略 ASCII
  - 非法字符、奇数 hex、残缺帧、未知 EtherType 无法切分时的失败记录
  - `pcap` 文件头和记录头正确，Wireshark 可打开
- 手工验证覆盖：
  - 设置页保存/重启后配置保留
  - 输出目录自动创建
  - 文件名符合 `yyyyMMddHHmmss-个数.pcap`
  - 配置 Wireshark 路径时提示并成功打开
  - 未配置时，系统存在 Wireshark 关联则提示；不存在则不提示

## Assumptions
- 界面语言默认中文。
- v1 不做安装包和自动更新，先交付可直接运行的 Release 构建产物。
- 文件名中的“报文个数”按成功写入的报文数计算，不包含失败项。
- 不支持非 Ethernet 链路类型写入；遇到明显不是完整 Ethernet 帧的输入，按失败项处理。
