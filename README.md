# 现场巡检问题标注

Unity Android AR 放置标记并上报问题，Go 保存到 SQLite，React 查看并修改处理状态。

源码仓库：[tofu1024/AR](https://github.com/tofu1024/AR)。

**当前版本为 1.2.1（versionCode 4）。Go 测试、React 联调、Unity 编译、稳定性检查、Android APK 构建及签名验证通过；手机已覆盖安装并启动。用户已确认光标变绿后成功放置。不同表面与跟踪恢复的完整验收、漂移量化、手机表单上报及两段演示视频仍待完成。**

当前 APK：unity/Builds/inspection.apk。已支持水平和竖直平面、稳定落点预览、确认放置及 Anchor 固定。实现细节见 [稳定落点预览实现说明](doc/稳定落点预览实现说明.md)。

## 1. 目录与版本

| 目录 | 内容 |
| --- | --- |
| backend | Go HTTP 接口、SQLite 存储、关键测试 |
| web | React 管理页面 |
| unity | Unity 项目、场景、C# 脚本、Android 配置工具 |
| scripts | Windows 启动脚本、可选浏览器联调脚本 |
| doc | 设计计划、验证记录、录屏清单和管理端截图 |

| 组件 | 版本 |
| --- | --- |
| Go | go.mod 最低 1.24.0；本机验证工具链 1.27.3 |
| SQLite Go 驱动 | modernc.org/sqlite 1.36.3，无需 CGO |
| Node.js | 本机 24.15.0；使用支持 Vite 7 的 Node 22.12+ 或 24 |
| npm | 本机 11.12.1 |
| React / TypeScript / Vite | 19.2.0 / 5.9.3 / 7.3.7 |
| Unity | 6000.0.62f1（Unity 6.0 LTS），已实际导入并构建 Android |
| AR Foundation / ARCore | 均为 6.0.8，已实际解析和编译 |
| Android 构建工具链 | OpenJDK 17.0.9、NDK r27c、SDK 36、Gradle 8.13 |

源码包含 web/package-lock.json、backend/go.sum、Unity Packages/packages-lock.json、自有 Assets 的 .meta 及实际生成的项目设置。运行缓存和 XR 临时资源不纳入版本控制。

## 2. 启动 Go

Windows 项目根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-backend.ps1
```

脚本优先使用 PATH 中的 Go，也支持本工作区已下载、校验 SHA256 的 .tools/go 工具链。分享源码时 .tools 被忽略，接收方需自行安装 Go。

脚本默认使用项目内 backend/data/issues.db 的绝对路径。服务日志会打印监听地址和实际数据库路径。停止使用 Ctrl+C。

也可手动启动：

```powershell
cd backend
$env:DB_PATH = Join-Path (Get-Location) 'data\issues.db'
go run .
```

| 环境变量 | 默认值/行为 |
| --- | --- |
| ADDR | 0.0.0.0:8080 |
| DB_PATH | 不经脚本且未设置时，使用系统用户配置目录下 ARInspection/data/issues.db；Windows 通常为 %APPDATA%/ARInspection/data/issues.db |
| ALLOWED_ORIGINS | http://localhost:5173,http://127.0.0.1:5173，多个来源用英文逗号分隔 |

默认数据库目录不依赖启动时的工作目录；如果自定义 DB_PATH，建议使用绝对路径。直接运行与使用脚本可能采用不同的默认数据库，**演示和重启时统一使用同一种启动方式及 DB_PATH**。数据库不纳入源码版本控制。

## 3. 启动 React

在第二个终端执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-web.ps1
```

或：

```powershell
cd web
npm ci
npm run dev
```

浏览器打开 [管理页面](http://localhost:5173)。默认后端为 http://localhost:8080。

需要修改时，将 web/.env.example 复制为 web/.env.local，修改 VITE_API_BASE_URL 后重启 Vite。浏览器来源变化时，也需调整 Go 的 ALLOWED_ORIGINS。页面在 localhost:5173 使用，默认开发服务不对局域网开放。

```powershell
cd web
npm run build
```

构建产物位于 web/dist。npm run preview 默认端口为 4173，若使用预览服务访问后端，需要将 http://localhost:4173 和/或 http://127.0.0.1:4173 加入 ALLOWED_ORIGINS 并重启 Go。

## 4. Unity Android 真机配置

### 环境

- 支持 ARCore 的 Android 真机，Android API 24 或以上，并安装/更新 Google Play Services for AR。
- Unity Hub 安装项目指定 Unity LTS，包含 Android Build Support、SDK & NDK Tools、OpenJDK。
- 电脑与手机处于同一个能够互相访问的局域网。

### 导入和构建

1. 在 Unity Hub 使用 Add project from disk，选择 Unity 项目。若原路径包含中文，先按下文创建英文目录联接，并选择 D:\Unity\InspectionProject 这个入口。
2. 等待 Package Manager 下载依赖并完成脚本编译。使用 Built-in Render Pipeline；本项目未配置 URP。
3. 执行菜单 **Inspection → Configure Android Project**。该工具配置 ARCore Loader、启动初始化、ARM64、IL2CPP、OpenGLES3、API 24、竖屏、HTTP、旧版 Input Manager、运行时材质所需 Shader 和构建场景。
4. 在 Build Profiles/Build Settings 切换到 Android。检查 Project Settings → XR Plug-in Management → Android 中 ARCore 已启用；检查 Project Validation 中是否还有设备/版本相关问题。
5. 打开 Assets/Scenes/Inspection.unity。场景入口为 InspectionBootstrap，运行时创建 AR Session、XR Origin、相机、平面管理器、射线管理器和表单。
6. 执行 **Inspection → Build Android APK**，输出 unity/Builds/inspection.apk；也可通过 Unity 的 Build And Run 安装到 USB 调试设备。
7. 首次运行允许相机权限。如果拒绝，在 Android 应用设置中重新授权后重启。

Editor 中无法据此证明真机 AR 正常，不使用普通 3D 模拟替代真机验收。

### 命令行构建和安装（Windows）

关闭该项目的 Unity 编辑器窗口，确保对应版本已激活许可证且安装 Android 模块。手机通过 USB 连接，启用 USB 调试并在手机上允许本机调试：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-install-android.ps1 -UnityPath "D:\Unity\6000.0.62f1\Editor\Unity.exe"
```

将示例路径替换为实际 Unity.exe。脚本通过 Unity batchmode 构建，日志在 .tools/run，成功后使用 ADB 安装并启动应用。支持 -BuildOnly 仅构建、-AdbPath 指定 ADB、-DeviceSerial 指定设备。自动查找 PATH、项目 .tools/platform-tools 和 Unity 自带的 ADB。不会把失败构建前遗留的 APK 当作新结果安装。

Android 工具链拒绝含中文等非 ASCII 字符的项目路径。本工作区使用目录联接提供英文入口，源码仍位于原项目目录。以实际路径为准执行一次：

```powershell
New-Item -ItemType Junction -Path 'D:\Unity\InspectionProject' -Target 'D:\面试题目\unity'
```

随后构建：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-install-android.ps1 -UnityPath 'D:\Unity\Editors\6000.0.62f1\Editor\Unity.exe' -ProjectPath 'D:\Unity\InspectionProject'
```

目录联接与原 Unity 文件夹是同一份内容，APK 最终仍在项目的 unity/Builds/inspection.apk。已有同名目录时先确认其目标，不要覆盖其他项目。

部分手机会以 INSTALL_FAILED_USER_RESTRICTED 拒绝 ADB 安装，且未必显示弹窗。脚本会将已构建的 APK 复制到手机 Download/AR_Inspection.apk，随后明确报出需要手动安装。请在手机文件管理器中打开该文件并按系统提示安装；这属于手机系统限制，不代表构建失败。USB 复制文件不会产生接收通知。

本机为节省 C 盘空间，实际构建时还设置了以下进程级环境变量。Gradle 缓存目录中的 gradle.properties 使用电脑现有代理，该配置位于源码目录外，其他环境应按自己的网络配置：

```powershell
$env:TEMP = 'D:\Unity\Temp'
$env:TMP = 'D:\Unity\Temp'
$env:UPM_CACHE_ROOT = 'D:\Unity\Caches\upm'
$env:GRADLE_USER_HOME = 'D:\Unity\Caches\gradle'
```

### 配置后端地址

1. 电脑启动 Go，用 ipconfig 确认当前局域网 IPv4。
2. 手机浏览器访问 http://电脑IP:8080/api/health，应看到 status 为 ok。
3. 如果不通，检查 Go 是否监听 0.0.0.0、Windows 防火墙是否放行私有网络 TCP 8080、Wi-Fi 是否开启客户端隔离。
4. 在手机 App 的 Backend URL 输入 http://电脑IP:8080。不要输入 localhost，也不要附加 /api。地址会保存到 PlayerPrefs。
5. 缓慢扫描地面、桌面或墙面；屏幕中心或点击位置作为瞄准点，黄色光标表示正在判断，稳定变绿后按 Place marker 创建锚定标记。再次选择目标并确认可替换标记。绿色边框表示正在跟踪的平面，不代表该落点已通过稳定性判断。
6. 输入标题和可选描述，点击优先级按钮循环选择 low、medium、high，点击 Submit issue。
7. 成功后表单清空，电脑 React 点击刷新查看问题；失败时保留输入与标记。

若提交提示 Request timeout，先核对 Backend URL。当前电脑局域网地址为 192.168.10.102，原 APK 预填的 192.168.1.100 是演示地址，不能直接使用。现有 APK 在界面修改并提交即可保存正确地址，无需重装。当前 APK 已取消首次启动时预填演示地址，首次安装需手动配置；覆盖安装仍会保留之前保存的地址。

手机端引导使用英文，减少未打包中文字体时的显示问题；输入内容和 API 支持 Unicode，脚本尝试使用系统 CJK 字体，具体设备的中文字体显示仍需真机确认。

## 5. API

所有 JSON 字段使用题目规定的小驼峰命名，时间由后端生成，格式为 UTC RFC 3339。位置是 Unity 当前会话中的世界坐标，单位为米。

| 方法 | 路径 | 请求 | 成功响应 |
| --- | --- | --- | --- |
| GET | /api/health | 无 | 200，{"status":"ok"} |
| POST | /api/issues | title、description、priority、position | 201，完整问题对象 |
| GET | /api/issues | 无 | 200，{"items":[问题对象]}，空列表为 [] |
| PATCH | /api/issues/{id}/status | {"status":"in_progress"} | 200，完整问题对象 |

创建请求：

```json
{
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 }
}
```

完整问题对象包含 id、title、description、priority、status、position、createdAt、updatedAt。id 由后端使用加密随机数生成，初始 status 为 open。

- title 必填，去除首尾空白后不能为空，最多 200 字符。
- description 可选，最多 5000 字符，缺省为空字符串。
- priority 必须为 low、medium、high。
- position 必须包含有限数值 x、y、z，坐标 0 合法。
- 更新的 status 必须为 open、in_progress、resolved，允许直接切换。
- 错误 JSON、非法字段或参数返回 400；不存在的问题返回 404；数据库操作失败返回 500。

错误示例：

```json
{"error":{"code":"INVALID_ARGUMENT","message":"标题不能为空"}}
```

## 6. 验证

```powershell
cd backend
go test -v ./...
go vet ./...
go build -o inspection-server.exe .
```

```powershell
cd web
npm ci
npm run build
npm audit --audit-level=moderate
```

可选浏览器联调在项目根目录执行（Windows，需要已安装 Edge/Chrome）：

```powershell
npm install --prefix .tools --no-save playwright
node scripts/check-web.cjs
```

先按上文构建后端 exe，并确保 5173、18080 未被其他进程占用。脚本自动启动独立后端和前端，使用 .tools 下的临时数据库，结束时停止自建进程。验证列表、创建后刷新、保存失败保留状态、成功修改、断网提示、实际重启持久化和首次加载断网；截图输出到 doc/管理端验证截图.png。没有 Edge/Chrome 时先通过 Playwright 安装 Chromium。

详细结果见 [验证记录](doc/验证记录.md)。真机验收和录屏步骤见 [真机验收与录屏清单](doc/真机验收与录屏清单.md)。

## 7. 完成情况和限制

已实现：四个 Go 接口、参数校验、SQLite 持久化、CORS；React 列表、状态更新和异常提示；Unity 的水平与竖直平面检测、跟踪状态过滤、稳定预览、按表面法线锚定标记、UI 触摸拦截、表单提交、地址设置和成功/失败处理源码。

已验证：Go 自动测试及编译、React 类型检查和生产构建、真实浏览器与真实 Go 进程联调及重启持久化；Unity 包解析、编辑器与玩家 C# 编译、IL2CPP ARM64 编译、Gradle 打包、APK 签名验证；手机通过局域网访问 Go 健康接口。

尚未验证/交付：不同表面、跟踪恢复、锚点替换与 UI 拦截的全面真机回归，漂移误差量化，手机表单到 Go 的真实上报和两段演示视频。APK 与演示视频未上传到源码仓库。

范围限制：只维护一个待提交标记；无登录、图片、历史标记恢复、跨设备定位、离线队列、实时推送和公网部署。网络超时可能发生“已写入但手机未收到响应”，手动重试前先在网页确认，当前未实现幂等提交。坐标不代表全球或园区统一坐标，不能跨会话重定位。原型面向可信局域网演示。

## 8. 交付

源码、文档和真实录制的两段 3～5 分钟视频整理后，通过仓库或文件分享交付。保留实际 Git 历史；不要提交工具链、缓存、运行数据库、密钥和个人敏感信息。AI 使用记录见 [AI_LOG.md](AI_LOG.md)，提交人需实际审查、验证并能解释核心代码。


## 9. 墙面标注补充（1.1）

题目背景涉及墙面破损，而必做条款只明确要求水平面检测。根据补充使用需求，1.1 在保留水平面检测的同时启用竖直平面，支持对已识别墙面直接放置标记。

- 检测配置：PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical。
- 命中范围：仍使用 PlaneWithinPolygon，仅允许在已识别的平面内部放置。
- 放置方向：使用命中姿态的局部 Y 轴作为表面法线，朝相机一侧偏移 4 厘米，使边长 8 厘米的立方体贴合表面。
- 上报坐标：仍保存原始命中点，不保存立方体中心偏移后的坐标；Go 和 React 接口无需调整。
- 首次启动不再预填演示后端 IP，需填写实际电脑地址；覆盖安装会保留之前保存的地址。

绿色线框仍由 ARCore 返回的平面边界绘制。缺乏纹理的纯白墙面可能无法稳定识别，开启竖直检测不保证任何墙面都能识别。墙面识别、贴合方向和完整上报仍需在真机上验证。


## 10. 稳定预览与锚点（1.2.1）

默认观察窗口约 0.4 秒、至少 6 个样本，窗口位置最大偏差 2.5 厘米、法线最大偏差 5°；这些是可调稳定性阈值，不是定位精度保证。切换平面、丢失命中、跟踪受限或应用暂停时重置判断。

预览采用指数平滑；确认时重新检测真实落点并创建 Anchor，提交读取锚点的表面位置。绿色光标且 Place marker 按钮可用时才能确认；没有有效锚定标记或跟踪受限时禁止提交。平面绿色轮廓仅作为扫描辅助，不会变成精确物体边界。

构建前自动运行 Assets/Editor/InspectionStabilityChecks.cs 的 11 项检查，也可在 Unity 命令行以 -executeMethod InspectionStabilityChecks.Run 单独运行。实际设备误差和漂移仍应通过同场景对比测试评估；本次未接入 Depth。
