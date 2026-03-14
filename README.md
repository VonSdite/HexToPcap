# HexToPcap

`HexToPcap` 是一个 Windows 桌面工具，用来把十六进制报文文本转换成可被 Wireshark 打开的 `.pcap` 文件。

项目使用 `WPF + .NET Framework 4.8`，当前输出格式固定为经典 `pcap`，链路层固定为 `Ethernet (DLT_EN10MB)`，不输出 `pcapng`。

## 功能

- 把普通十六进制文本转换为 `.pcap`
- 支持 `tcpdump -vv -nn -XX` 风格的十六进制输出
- 支持单个报文跨多行输入
- 支持多个报文用空行分隔
- 在没有空行时，按“新的一行起始处出现常见 Ethernet 头”切分新报文
- 支持 `0x` / `0X` 前缀，以及行首第一个 `token:` 展示前缀的忽略处理
- 支持多层 VLAN / QinQ 封装报文作为新报文起点识别
- 不完整或协议字段不合法的报文片段也会照样导出
- 转换后可按配置或系统关联调用 Wireshark 打开结果文件

## 运行环境

- Windows
- `.NET Framework 4.8` 运行时
- 构建时需要可用的 `MSBuild.exe`
  - 可来自 Visual Studio
  - 或 .NET Framework / Build Tools

## 使用方式

1. 启动程序：`build\Release\HexToPcap.exe`
2. 在主窗口粘贴十六进制文本
3. 如需修改输出目录或 Wireshark 路径，点击顶部保存位置右侧的设置图标
4. 点击 `转换`
5. 成功后会在输出目录生成 `.pcap` 文件，文件名格式为 `yyyyMMddHHmmss-报文个数.pcap`

默认输出目录为：

```text
%USERPROFILE%\Documents\HexToPcap
```

如果设置了 `WiresharkPath`，或者系统 `.pcap` 关联明确指向 `wireshark.exe`，转换成功后程序会提示是否直接打开生成文件。

## 支持的输入格式

### 1. 普通十六进制文本

可以有空格、Tab、换行。一个报文可以写成一行或多行，也支持每个 token 带 `0x` / `0X` 前缀。

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

如果没有空行，程序只会在“新的一行起始处识别到常见 Ethernet 头”时开始一个新报文，不会在一行中间猜测切包。

如果某一行的第一个 token 以冒号结尾，例如 `0x0000:`、`0x0010:`、`gdb_print:`，程序会先忽略这个展示前缀，再继续提取后面的十六进制内容。

### 2. `tcpdump -vv -nn -XX` 输出

程序会先忽略每一行行首第一个以冒号结尾的 token，再提取后面的十六进制部分；在已经开始提取十六进制内容后，遇到第一个非十六进制 token 会停止，从而忽略右侧 ASCII。

```text
12:00:00.000000 IP sample > sample: payload
        0x0000:  0011 2233 4455 6677 8899 aabb 0800 4500  .."3DUfw......E.
        0x0010:  0018 0001 0000 4001 0000 c0a8 0101 c0a8  ......@.........
        0x0020:  0102 dead beef                           ........
```

`tcpdump` 中的偏移值只会被当作展示信息忽略，不参与分包判断；偏移不连续、跳变或不是从 `0x0000` 开始时，也不会阻止导出。

## 解析规则

- 空行会被视为显式分包边界
- 普通十六进制输入支持忽略 `0x` / `0X` 前缀，以及每一行行首第一个 `token:`
- 十六进制字符数量为奇数时，会自动在末尾补 `0`
- 没有空行时，只在新的一行起始处识别到常见 Ethernet 头时切分新报文
- 常见 Ethernet 头当前覆盖 `IPv4`、`IPv6`、`ARP`、`VLAN`、`QinQ`
- 不再依赖 IPv4 / IPv6 / ARP 长度字段来判断成功或失败
- 只要提取到了十六进制字节，就会导出对应报文片段
- 仅支持写出 `Ethernet` 链路层报文
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

- 单个报文与跨多行报文解析
- 空行分隔与按新行以太头切包
- `0x` / `0X` 前缀与行首前缀 token 忽略处理
- 奇数长度 token 自动补 `0`
- 不完整报文片段的直接导出
- `tcpdump -XX` 输入解析、ASCII 忽略与偏移跳变兼容处理
- `pcap` 文件头与输出命名格式

## 项目结构

```text
src/
  HexToPcap/        WPF 桌面应用
  HexToPcap.Core/   解析器、pcap 写入器、核心模型
tests/
  HexToPcap.Tests/  控制台测试程序
docs/
  plan1/PLAN.md     实施说明
```

## 已知限制

- 只支持输出经典 `pcap`，不支持 `pcapng`
- 只支持 `Ethernet` 链路层
- 自动拆包不覆盖所有 `EtherType`，只识别当前已实现的常见 Ethernet 头
- 自动拆包依赖文本行边界；如果多个报文被揉进同一行，程序不会在行内猜测切分
