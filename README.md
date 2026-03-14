# HexToPcap

`HexToPcap` 是一个 Windows 桌面工具，用来把十六进制报文文本转换成可被 Wireshark 打开的 `.pcap` 文件。

项目使用 `WPF + .NET Framework 4.8`，当前输出格式固定为经典 `pcap`，链路层固定为 `Ethernet (DLT_EN10MB)`，不输出 `pcapng`。

## 功能

- 把普通十六进制文本转换为 `.pcap`
- 支持 `tcpdump -vv -nn -XX` 风格的十六进制输出
- 支持单个报文跨多行输入
- 支持多个报文用空行分隔
- 在没有空行时，按 `Ethernet / IPv4 / IPv6 / ARP` 长度字段自动尝试拆包
- 支持单层 VLAN 封装报文的自 动拆包
- 转换后可按配置或系统关联调用 Wireshark 打开结果文件
- 对无法解析的报文片段给出错误原因和预览

## 运行环境

- Windows
- `.NET Framework 4.8` 运行时
- 构建时需要可用的 `MSBuild.exe`
  - 可来自 Visual Studio
  - 或 .NET Framework / Build Tools

## 使用方式

1. 启动程序：`build\Release\HexToPcap.exe`
2. 在主窗口粘贴十六进制文本
3. 如需修改输出目录或 Wireshark 路径，打开 `文件 -> 设置`
4. 点击 `转换`
5. 成功后会在输出目录生成 `.pcap` 文件，文件名格式为 `yyyyMMddHHmmss-报文数.pcap`

默认输出目录为：

```text
%USERPROFILE%\Documents\HexToPcap
```

如果设置了 `WiresharkPath`，或者系统 `.pcap` 关联明确指向 `wireshark.exe`，转换成功后程序会提示是否直接打开生成文件。

## 支持的输入格式

### 1. 普通十六进制文本

可以有空格、Tab、换行。一个报文可以写成一行或多行。

```text
00 11 22 33 44 55 66 77 88 99 AA BB 08 00
45 00 00 18 00 01 00 00 40 01 00 00 C0 A8 01 01
C0 A8 01 02 DE AD BE EF
```

多个报文推荐使用空行分隔：

```text
00 11 22 33 44 55 66 77 88 99 AA BB 08 00 45 00 00 18 00 01 00 00 40 01 00 00 C0 A8 01 01 C0 A8 01 02 DE AD BE EF

00 11 22 33 44 56 66 77 88 99 AA BC 08 06 00 01 08 00 06 04 00 01 00 11 22 33 44 56 C0 A8 01 56 66 77 88 99 AA BC C0 A8 01 57
```

如果没有空行，程序会按报文头中的长度字段尝试自动拆分连续报文。

### 2. `tcpdump -vv -nn -XX` 输出

程序会识别 `0x0000:` 这类偏移行，只提取中间的十六进制部分，忽略右侧 ASCII。

```text
12:00:00.000000 IP sample > sample: payload
        0x0000:  0011 2233 4455 6677 8899 aabb 0800 4500  .."3DUfw......E.
        0x0010:  0018 0001 0000 4001 0000 c0a8 0101 c0a8  ......@.........
        0x0020:  0102 dead beef                           ........
```

当偏移重新回到 `0x0000` 时，会视为一个新报文开始。

## 解析规则

- 空行会被视为显式分包边界
- 普通十六进制文本中出现非十六进制字符，或十六进制字符数量为奇数，会被记为错误
- 自动拆包当前只基于 `Ethernet`、`IPv4`、`IPv6`、`ARP`
- 仅支持写出 `Ethernet` 链路层报文
- 遇到无法根据 `EtherType` 推断长度的连续报文，且没有空行边界时，会记为失败而不是猜测切分
- 没有成功报文时不会生成输出文件

## 构建与测试

仓库根目录提供了构建脚本：

```powershell
.\build.ps1 -Configuration Release
```

或：

```cmd
build.cmd
```

常用参数：

- `-Configuration Debug|Release`
- `-SkipTests`：只构建，不跑测试
- `-StopRunningApp`：如果正在运行的 `HexToPcap.exe` 占用了输出目录，先停止再构建

这个仓库当前没有 `.sln` 文件，`build.ps1` 会自动退回到直接构建项目文件。

构建产物位置：

- 应用程序：`build\Release\HexToPcap.exe`
- 测试程序：`build\tests\Release\HexToPcap.Tests.exe`

已覆盖的测试包括：

- IPv4 / IPv6 / ARP 报文解析
- 空行分隔与无空行自动拆包
- VLAN 报文自动拆包
- `tcpdump -XX` 输入解析
- 非法字符、奇数长度十六进制、残缺报文的错误处理
- `pcap` 文件头与输出命名格式

## 项目结构

```text
src/
  HexToPcap/        WPF 桌面应用
  HexToPcap.Core/   解析器、pcap 写入器、核心模型
tests/
  HexToPcap.Tests/  控制台测试程序
docs/
  PLAN.md           实施说明
```

## 已知限制

- 只支持输出经典 `pcap`，不支持 `pcapng`
- 只支持 `Ethernet` 链路层
- 自动拆包不覆盖所有 EtherType，只处理当前已实现的 `IPv4`、`IPv6`、`ARP`
- VLAN 自动拆包按当前实现只处理单层 VLAN 头
