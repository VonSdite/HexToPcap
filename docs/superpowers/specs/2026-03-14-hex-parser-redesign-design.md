# Hex Parser Redesign Design

## Summary

本次修改只重构“输入文本如何提取为报文并切分边界”的策略，不改 `pcap` 输出格式、不改桌面端的总体交互流程。

新的解析器以“尽可能提取、稳定切包、永不因为内容不合法而放弃输出”为核心原则：

- 空行是最高优先级的显式报文边界。
- 没有空行时，只在“新的一行起始处识别到常见 Ethernet 头”时切分新报文。
- 不再依赖 IPv4 / IPv6 / ARP 长度字段来判断成功或失败。
- 不完整报文、协议字段不合法报文、未知载荷报文都要输出。
- `0x` / `0X` 前缀要忽略。
- 支持 `tcpdump -XX` 风格输入，本质上按规则提取其中的十六进制部分。
- 不再存在“解析失败列表”这个业务概念；解析器只有“提取出多少报文”和“一个都没提取出来”两种结果。

## Goals

- 让程序符合新的用户规则，而不是继续以协议合法性驱动解析。
- 在不大改调用链的前提下，调整核心解析器行为。
- 保持输出稳定、可预测，避免在负载中误切或漏切。

## Non-Goals

- 不支持“任意自然语言文本中抽取所有十六进制字符”。
- 不扩展到非 Ethernet 链路层输出。
- 不新增 `pcapng`、自定义链路类型、协议深度解析等功能。
- 不把“报文修复”做成复杂纠错器；仅支持用户明确要求的奇数字符补 `0`。

## User Rules Restated

本次实现必须满足以下确认过的规则：

1. 多个字符串之间的空行代表分割报文，空行肯定隔开了报文。
2. 多个报文之间没有空行时，以 Ethernet 头来区分边界；一个新的常见 Ethernet 头就是一个新的报文。
3. 两个报文的内容不会揉在同一行里，因此不需要在一行中间切包。
4. 一个报文即使不完整或者不合法，也要输出；不需要错误列表。
5. 输入中的 `0x`、`0X` 前缀需要忽略。
6. 支持 `tcpdump -XX` 风格输入，本质上是提取其中的十六进制内容。
7. 普通输入中的杂字符不支持；目标是不再有“解析失败场景”，而不是把任意文本都当作 hex 源。
8. 当提取出的十六进制字符数量为奇数时，自动在末尾补 `0`。
9. “常见 Ethernet 头”只认当前产品需要覆盖的常见类型，不做宽泛的 `EtherType >= 0x0600` 推断。

## Architecture

### Core Direction

将当前解析器从“校验 + 推断长度 + 输出成功/失败”改成“提取 + 边界识别 + 输出全部分片”。

现有 `HexInputParser` 仍然作为唯一入口，但内部逻辑需要拆成两个清晰阶段：

1. 输入归类与 hex 提取
2. 依据文本边界规则组装 packet

### Units

建议在现有 `HexInputParser` 文件内先完成行为重构；如果实现后方法明显膨胀，再拆辅助私有方法或小型内部 helper。边界如下：

- `Parse(string input)`
  - 负责统一换行、按空行分 block、分派 block 解析方式。
- `ParsePlainBlock(...)`
  - 负责普通 hex 文本的逐行提取与按行边界切包。
- `ParseTcpdumpBlock(...)`
  - 负责 `tcpdump -XX` 风格文本的偏移识别与 hex 区提取。
- `TryRecognizeEthernetHeaderAtLineStart(...)`
  - 只负责判断某一行提取出的起始字节是否可视为“新报文起点”。
- `NormalizeHexToken(...)` / `ExtractHexBytesFrom...`
  - 只负责 token 清洗、`0x` 去前缀、奇数补零、转字节。

这几个单元职责单一，便于测试时直接从行为层验证。

## Detailed Design

### 1. Top-Level Block Splitting

先统一换行符，将输入视为行序列。

- 连续非空行组成一个 block。
- 任何空行都会结束当前 block。
- 连续多个空行只代表多个显式边界，不生成空报文。
- 只有 block 内至少提取到一个字节，才输出 packet。

空行是最高优先级边界；后续任何自动切包都只能发生在 block 内部。

### 2. Block Type Detection

每个 block 先判断是否包含 `tcpdump` 偏移行：

- 匹配 `^\s*0x[0-9A-Fa-f]+:` 的行，视为 `tcpdump -XX` block。
- 只要一个 block 内出现这种偏移行，整个 block 按 tcpdump 规则处理。
- 其余 block 按普通 hex 文本处理。

这样可以兼容：

- 标准 `tcpdump -XX`
- 带时间戳/协议说明头的 `tcpdump -XX`
- 紧凑的纯 hex 多行文本

### 3. Plain Hex Parsing

普通 block 采用“按行提取、按行切包”的策略。

#### 3.1 Token Extraction

每一行按空白拆成 token。

对每个 token：

- 如果以 `0x` 或 `0X` 开头，则去掉该前缀。
- 去前缀后必须全部是十六进制字符；否则该 token 直接忽略，不纳入报文。
- 如果 token 的十六进制字符数量为奇数，则在末尾补 `0`。
- 每两个 hex 字符组成一个字节。

设计含义：

- 支持 `0x0011 0x2233` 这类输入。
- 支持 `A`、`123` 这类奇数位输入自动补全。
- 不支持把普通文本中的随机字符揉进 hex 流。

#### 3.2 Line-Level Packet Boundary Recognition

每一行提取出一个 `byte[]` 片段，并记录“该行如果并入当前 block，它在整体字节流中的起始偏移”。

切包规则：

- block 的第一条有字节内容的行，必然开始一个新 packet。
- 从第二条有字节内容的行开始，只有在“该行自身的起始字节可识别为常见 Ethernet 头”时，才开始新 packet。
- 否则该行继续并入当前 packet。

由于用户明确说明“两个报文内容不会揉在同一行里”，因此：

- 不在一行中间扫描边界。
- 不在已累积 packet 的内部扫描二次边界。

这可以显著降低在载荷中误识别出伪 Ethernet 头的风险。

#### 3.3 Common Ethernet Header Recognition

新报文的识别只覆盖当前产品已支持/已知的常见类型：

- `0x0800` IPv4
- `0x86DD` IPv6
- `0x0806` ARP
- `0x8100` VLAN
- `0x88A8` QinQ / Provider Bridging
- `0x9100` 常见扩展 VLAN 封装

识别条件：

- 该行提取出的字节数至少为 14 字节，足以包含 Ethernet 头。
- 第 13-14 字节（从 1 开始计数）对应的 EtherType 属于上表之一。

这里不继续校验：

- IPv4 版本/头长/总长
- IPv6 payload length
- ARP 长度字段
- VLAN 内层协议完整性

这些内容不再用于判定“是不是可输出报文”，只作为 Wireshark 后续解码时自行处理的内容。

### 4. Tcpdump Parsing

`tcpdump -XX` block 按“偏移行驱动”的方式解析。

#### 4.1 Recognized Lines

偏移行格式：

- `0x0000:`
- `0x0010:`
- 允许前导空白

非偏移行处理：

- 在第一个偏移行出现前，说明行、时间戳行、协议描述行全部忽略。
- 在偏移行之间出现的普通说明文本也忽略，不报错。

#### 4.2 Hex Region Extraction

对于偏移行，只提取冒号后面的 hex 区。

处理原则：

- 先按空白切 token。
- 只吸收连续的 hex token。
- 一旦遇到第一个非 hex token，后续内容全部视为 ASCII 展示区并忽略。
- token 长度可为 1~4 个 hex 字符；奇数位同样补 `0`。

这样可以兼容常见形式：

- `0011 2233 4455`
- `00 11 22 33`
- 右侧跟随 `..E.` 这类 ASCII 预览

#### 4.3 Tcpdump Packet Boundary

`tcpdump` block 的 packet 边界规则：

- 第一个提取到字节的偏移行开始一个新 packet。
- 后续如果再次看到 `offset == 0`，则先结束当前 packet，再开始一个新 packet。
- 偏移不连续、重复、跳跃都不算错误，不阻止输出。
- 如果 block 结束时还有累积字节，则直接输出最后一个 packet。

这符合“永不因为格式不完美而失败”的要求。

### 5. No Parse Failure Model

新的解析模型不再区分“成功 packet”和“失败 packet”。

可接受的结果只有：

- 提取出至少 1 个 packet
- 0 个 packet

因此：

- 不再为“不完整”“未知 EtherType”“长度不匹配”创建错误项。
- 仅当整个输入没有提取出任何字节时，调用方才视为“没有可转换报文”。

## Data Model Impact

### ParseResult

为减少调用链改动，`ParseResult` 暂时保留现有结构：

- `SuccessfulPackets`: 存放全部输出 packet
- `Errors`: 始终为空集合

后续如果需要彻底清理命名，再单独做一次轻量重构；本次不把接口改动扩大化。

### PacketParseError

模型先保留，避免 UI 和绑定一次性大改。

短期行为：

- 解析器不再填充 `PacketParseError`
- UI 读到的 `Errors` 永远为空

## UI Behavior Changes

`MainWindowViewModel.Convert()` 的成功条件改为：

- 只要 `ParseResult.SuccessfulPackets.Count > 0`，就写出 `pcap`
- 不再根据解析错误数量构建失败摘要

摘要文案调整为：

- 有报文时：显示“成功导出 N 个”以及文件名
- 无报文时：显示“未识别到报文”

错误列表区域短期保留现有结构，但不会再出现解析失败项。

这保证：

- 不需要同步重做整个 UI
- 用户感知上符合“没有解析失败场景”

## Testing Strategy

### Replace Failure-Oriented Tests

以下现有测试需要改写或删除其“失败预期”：

- 非法字符导致失败
- 奇数 hex 导致失败
- 未知 EtherType 拼接导致失败
- 残缺报文进入错误列表

这些行为在新规则下都不再成立。

### Required Parser Tests

至少覆盖以下行为：

1. 空行一定分包。
2. 多个空行不会产生空 packet。
3. 没有空行时，只有“新的一行起始处出现常见 Ethernet 头”才分包。
4. 同一个 packet 跨多行时不会被误切。
5. 不完整 IPv4 片段也会输出。
6. 不完整 VLAN / QinQ 片段也会输出。
7. 未知载荷只要已形成片段也会输出。
8. 普通 hex 输入中的 `0x` / `0X` 前缀会被忽略。
9. 普通 hex 输入中奇数位 token 会自动补 `0`。
10. 普通 hex 输入中的杂字符 token 会被忽略，而不是导致整个 block 失败。
11. `tcpdump -XX` 能忽略描述行，仅提取偏移行 hex 区。
12. `tcpdump -XX` 能忽略右侧 ASCII 区。
13. `tcpdump -XX` 在 `0x0000:` 重置时开始新 packet。
14. `tcpdump -XX` 偏移不连续时仍然输出。
15. 整个输入没有任何可提取 hex 时返回 0 个 packet。

### Existing Writer Tests

`PcapWriter` 相关测试基本不需要改动。

仍需保留验证：

- `pcap` 全局头正确
- 记录头长度字段正确
- 文件名中 packet 个数等于实际输出 packet 数

## Edge Cases

### Empty Input

- 输入为空或全空白时，不输出 packet。

### Mixed Blocks

- 某个 block 一旦被识别为 tcpdump block，就整块按 tcpdump 规则处理。
- 不支持在同一个 block 内同时混用普通 hex 行和需要参与提取的非 tcpdump 样式说明文本。

### False Positive Boundaries

普通 block 只在“新的一行起始位置”识别新 Ethernet 头，这是降低误切的核心约束。

该策略建立在已确认前提上：

- 两个报文不会揉在同一行

如果未来输入来源打破这个前提，需要另行设计“行内扫描边界”版本，本次不做。

## Migration Notes

从旧策略迁移到新策略后，最明显的行为变化是：

- 以前会报“失败”的输入，现在会被导出为 packet。
- 以前依赖 IPv4 / IPv6 / ARP 长度字段自动切分的场景，现在改为依赖空行或新一行的常见 Ethernet 头。
- 以前 `Errors` 会有内容，现在固定为空。

这属于有意行为变化，不是兼容性 bug。

## Implementation Scope

本次实现预计主要修改以下位置：

- `src/HexToPcap.Core/Services/HexInputParser.cs`
- `src/HexToPcap/ViewModels/MainWindowViewModel.cs`
- `tests/HexToPcap.Tests/Program.cs`
- 视情况更新 `README.md`

不预期需要修改：

- `PcapWriter`
- 设置存储
- Wireshark 打开逻辑

## Open Questions

无。本次设计涉及的边界条件已通过用户确认：

- 杂字符不支持
- 奇数位自动补 `0`
- 新报文边界只认常见 Ethernet 头
