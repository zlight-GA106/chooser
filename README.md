# 选择器 by zlight106

轻量的选择题练习与课堂考试工具，提供 Windows 单文件离线版和手机、平板、电脑通用的网页版。Go 标准库服务端内嵌网页、题库、图标及帮助，运行时无需安装 Go、Node.js、Python 或数据库。

[下载最新版本](https://github.com/zlight-GA106/chooser/releases/latest) · [v1.4.0 发布页](https://github.com/zlight-GA106/chooser/releases/tag/v1.4.0) · [使用说明](使用说明.md) · [题库 XML 示例](assets/example.xml)

## 功能与数据保存

- 单选、多选、T/F 判断、按范围练习、随机考试；每题 1 分，结束后显示成绩。
- 支持“显示答案”和“立即评分”，错题按答错次数统计，自动保存答题进度。
- 题库支持新建、导入、名称或内容搜索、分页查看、编辑及校验 XML、重命名、导出、删除。
- 重命名保留记录；编辑保留未改变题目的错题次数，影响当前进度时先提示确认。
- 内置“2025新版大信_选择题”196 题、XML 写作指南与完整示例。
- 原生 HTML/CSS/ES5 JavaScript，无前端框架和外部 CDN；缓存、gzip、ETag/304 节省流量。

**数据属于各自设备。** 网页题库、错题、设置和进度优先保存在当前浏览器的 IndexedDB，导入、管理、答题和评分不上传服务器。没有账号、UID、多端同步、统一收卷或集中成绩后台。老师在自己浏览器创建题库不会自动分发给学生；可导出 XML 供学生导入，或修改内置题库后重新发布。

手机兼容目标为 Android 8–16 上的 HTML5 浏览器，基础目标为 Chrome 61 或更新版本。已用 320/360 像素浏览器视口验证管理、编辑、保存、搜索和重命名：主要触摸目标至少 44 像素，按钮换行，XML 字体 16 像素、长文本换行、保存栏可停靠。尚未逐个 Android 版本进行实体设备测试。

## 发布附件与部署选择

在 [Releases](https://github.com/zlight-GA106/chooser/releases) 下载编译附件，源码 ZIP 中不包含发布程序。

| 文件 | 用途 |
| --- | --- |
| `chooser-desktop-v1.4.0.exe` | Windows 桌面离线版，直接发给老师 |
| `chooser-server-v1.4.0-windows-amd64.exe` | Windows x86-64 网页服务端 |
| `chooser-server-v1.4.0-linux-amd64` | Linux x86-64 网页服务端 |
| `chooser-server-v1.4.0.zip` | Windows/Linux 服务端、部署说明和 Go 许可证合集 |
| `chooser-web-v1.4.0.zip` | 已有 Nginx、Apache、宝塔或静态托管部署 |
| `SHA256.txt` | 发布附件 SHA-256 校验值 |

Linux 先执行 `uname -m`，输出 `x86_64` 可使用 amd64 附件；ARM/aarch64 需从源码编译对应架构。可用 `sha256sum 文件名` 或 PowerShell 的 `Get-FileHash -Algorithm SHA256 -LiteralPath '文件名'` 与 `SHA256.txt` 比较。

Release 使用英文附件名；从源码构建或解压服务端合集时，程序也可能采用中文文件名，功能相同。

推荐：**Linux Go 程序 + systemd 常驻 + Nginx 域名 HTTPS**。局域网临时使用可直接运行 Go，访问 `http://服务器IP:8088/`；已有网站环境也可选择纯静态部署。

## 一、Linux 快速启动

下面以有 sudo 权限的 Ubuntu/Debian 用户为例；其他发行版使用对应包管理工具。域名 `quiz.example.com`、IP 和上传路径均需换成自己的值。

1. 下载 Linux 附件，在本机重命名为 `chooser`。通过 SFTP、SCP 或面板上传至服务器 `~/chooser-upload/chooser`，上传前可创建目录：

   ```bash
   mkdir -p ~/chooser-upload
   ```

2. SSH 登录服务器，执行：

   ```bash
   chmod 755 ~/chooser-upload/chooser
   ~/chooser-upload/chooser -version
   ~/chooser-upload/chooser -addr 0.0.0.0:8088
   ```

3. 另开 SSH 窗口验证：

   ```bash
   curl -f http://127.0.0.1:8088/healthz
   curl -f http://127.0.0.1:8088/api/version
   ```

   应返回健康状态、版本 `1.4.0` 和 `backend: go`。手机访问 `http://服务器实际IP:8088/`；公网用公网 IP，课堂局域网用局域网 IP。`0.0.0.0` 是监听地址，不能作为客户端地址。

4. 在云安全组和系统防火墙同时放行 TCP 8088。若已启用 UFW：

   ```bash
   sudo ufw allow 8088/tcp
   sudo ufw status
   ```

   不需要为此启用或重置已有防火墙；其他防火墙配置相同端口即可。

终端断开后临时进程可能停止。按 `Ctrl+C` 停止测试，再设置下面的常驻服务，避免端口被两个进程占用。

## 二、systemd 常驻与开机启动

正式域名部署让 Go 监听 `127.0.0.1:8088`，Nginx 对外提供 80/443。

安装程序与专用系统用户：

```bash
sudo install -d -m 755 /opt/chooser
sudo install -m 755 ~/chooser-upload/chooser /opt/chooser/chooser
id -u chooser >/dev/null 2>&1 || sudo useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin chooser
```

新建服务单元：

```bash
sudo tee /etc/systemd/system/chooser.service >/dev/null <<'EOF'
[Unit]
Description=Chooser Web Service
After=network.target

[Service]
Type=simple
User=chooser
Group=chooser
WorkingDirectory=/opt/chooser
ExecStart=/opt/chooser/chooser -addr 127.0.0.1:8088
Restart=on-failure
RestartSec=3
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now chooser
sudo systemctl status chooser --no-pager
curl -f http://127.0.0.1:8088/healthz
```

程序读取内嵌页面，无需写入服务器数据库。常用维护命令：

```bash
sudo systemctl restart chooser
sudo systemctl stop chooser
sudo journalctl -u chooser -n 100 --no-pager
sudo journalctl -u chooser -f
```

仅局域网使用且不配置 Nginx 时，将 `ExecStart` 地址改为 `0.0.0.0:8088` 并放行 8088。修改服务单元后执行 `sudo systemctl daemon-reload` 和 `sudo systemctl restart chooser`。单元规则见 [systemd 官方文档](https://www.freedesktop.org/software/systemd/man/latest/systemd.service.html)。

## 三、域名、Nginx 与 HTTPS

### 域名与 HTTP 代理

将域名 A 记录指向服务器公网 IPv4；仅实际提供 IPv6 时配置 AAAA。在云安全组与防火墙放行 TCP **80、443**。Go 监听回环地址时，无需对公网开放 8088。已有宝塔等面板时跳到第四节，避免安装第二套 Nginx。

Ubuntu/Debian 安装 Nginx：

```bash
sudo apt-get update
sudo apt-get install -y nginx
```

替换示例域名后添加配置：

```bash
sudo tee /etc/nginx/conf.d/chooser.conf >/dev/null <<'EOF'
server {
    listen 80;
    server_name quiz.example.com;

    location / {
        proxy_pass http://127.0.0.1:8088;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Connection "";
    }
}
EOF

sudo nginx -t
sudo systemctl enable --now nginx
sudo systemctl reload nginx
```

确认 `http://quiz.example.com/` 可访问。Go 已处理 gzip、ETag 与缓存响应头，代理保留即可；不要给首页和 `sw.js` 统一设置长期缓存。反代规则见 [Nginx 官方文档](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_pass)。

### 申请与续期证书

按 [Certbot 官方安装向导](https://certbot.eff.org/instructions?ws=nginx&os=snap) 选择实际系统和 Nginx。以下适用于**已配置 snapd** 的服务器；没有 snapd 或已有其他 Certbot 安装时，先按向导安装对应环境，避免混装：

```bash
sudo snap install --classic certbot
sudo /snap/bin/certbot --nginx -d quiz.example.com
sudo /snap/bin/certbot renew --dry-run
```

按提示申请证书和 HTTPS 跳转，完成后访问 `https://quiz.example.com/`。HTTP 验证需 DNS 正确且外部能访问 80；无法开放 80 时使用证书工具的 DNS 验证。续期依靠证书工具的计划任务或 systemd timer，应确认测试续期通过。

HTTPS 下可注册 Service Worker 缓存页面和资源。普通局域网 HTTP 仍可保存题库和进度，但手机通常不能为该地址启用 Service Worker，不能保证整页离线重新打开。

已有受信任证书时，也可由 Go 直接提供 HTTPS：

```bash
./chooser -addr 0.0.0.0:8443 -cert /path/to/fullchain.pem -key /path/to/privkey.pem
```

放行 8443，访问 `https://证书对应域名:8443/`。运行用户需能读证书链和私钥；续期后重启 Go 加载新证书。`-cert` / `-key` 必须一起提供。标准 443 端口优先采用 Nginx 方案。

## 四、宝塔面板部署

面板名称可能随版本变化，实际配置如下：

1. 按第二节启动 Go 常驻服务，监听 `127.0.0.1:8088`；先用 `curl -f http://127.0.0.1:8088/healthz` 验证。
2. 使用面板安装的 Nginx，添加实际域名的网站，无需 PHP 项目或数据库。
3. 添加网站“反向代理”：目标 URL `http://127.0.0.1:8088`、代理目录 `/`、发送域名保留当前域名，先关闭面板额外代理缓存。
4. DNS 指向服务器；安全组、防火墙和面板安全设置确认 80/443 可访问。
5. 在“SSL”申请或部署域名证书，测试续期；HTTPS 可访问后启用 HTTP 跳转 HTTPS。
6. 手机检查首页、帮助、导入、答题和刷新后恢复数据。

反代方式无需另外上传 `web/dist`。纯静态部署则使用第七节，省略 Go 进程与反代。

## 五、部署到子目录 `/chooser/`

Go 配置路径前缀：

```bash
./chooser -addr 127.0.0.1:8088 -base-path /chooser/
```

systemd 将参数加入 `ExecStart` 后重载、重启。Nginx 在现有域名的 `server` 内添加：

```nginx
location = /chooser {
    return 308 /chooser/;
}
location /chooser/ {
    proxy_pass http://127.0.0.1:8088;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Connection "";
}
```

`proxy_pass` 端口后**没有 `/`**，保留上游要求的 `/chooser/` 前缀；检查 `sudo nginx -t` 后重载 Nginx。访问 `https://quiz.example.com/chooser/`，健康与版本检查变为 `/chooser/healthz`、`/chooser/api/version`。不要反代剥掉前缀、Go 却仍要求前缀。

## 六、Windows 网页服务部署

此处用 Go 服务端 EXE，与桌面离线版 EXE 不同。

1. 下载 `chooser-server-v1.4.0-windows-amd64.exe`，放到如 `C:\Apps\Chooser\`，可重命名为 `chooser-server.exe`。无需安装 Go 或 .NET。
2. PowerShell 运行：

   ```powershell
   Set-Location 'C:\Apps\Chooser'
   .\chooser-server.exe -addr '0.0.0.0:8088'
   ```

3. 本机访问 `http://127.0.0.1:8088/`，手机访问 `http://这台电脑的局域网IP:8088/`；公网服务器需同时放行安全组。
4. 在 Windows 防火墙高级设置的入站规则允许此程序或 TCP 8088，按实际网络范围限制来源。
5. 开机运行可在任务计划程序创建任务：触发器“启动时”，程序为 EXE 完整路径，参数 `-addr 0.0.0.0:8088`，起始位置 `C:\Apps\Chooser`；使用专用普通账户，按需选“不管用户是否登录都运行”和失败后重启。创建后手动运行、重启后再次验证网页。

临时窗口关闭会停止服务。已有 HTTPS 反代时让 Go 监听 `127.0.0.1:8088`，也可使用前文 `-cert` / `-key`。

## 七、纯静态网页部署

已有 Nginx、Apache、宝塔网站或静态托管时，无需另开 Go：

1. 下载并解压 `chooser-web-v1.4.0.zip`。
2. 上传**解压后的全部文件内容**，让网站目录直接包含 `index.html`、`help.html`、`app.js`、`core.js`、`storage.js`、`sw.js`、`default.xml`、图标等，不要只上传 ZIP。
3. 默认文档设为 `index.html`，配置域名和 HTTPS。根目录或 `/chooser/` 子目录均可，相对文件位置保持不变。
4. 访问 HTTP/HTTPS 地址，不要双击 HTML 使用 `file://`。

独立 Nginx 静态站点 HTTP 示例，修改域名和实际解压目录后，HTTPS 按第三节配置：

```nginx
server {
    listen 80;
    server_name quiz.example.com;
    root /var/www/chooser;
    index index.html;

    location ~ (^|/)\. {
        deny all;
    }
    location ~ ^/(?:index\.html|help\.html|sw\.js|guide\.json)$ {
        add_header Cache-Control "no-cache";
        try_files $uri =404;
    }
    location ~* \.(?:css|js|svg|png|ico|webmanifest)$ {
        expires 1d;
        try_files $uri =404;
    }
    location / {
        add_header Cache-Control "no-cache";
        try_files $uri $uri/ =404;
    }
    gzip on;
    gzip_vary on;
    gzip_types text/css application/javascript application/json application/xml image/svg+xml;
}
```

此配置用于独立域名根目录，不要和同域名 Go 反代配置重复启用。静态部署没有 `/healthz` / `/api/version`，这两个接口属于 Go。包中的 `_headers` 仅适用于支持该格式的平台，Nginx/Apache 不会自动应用。

## 八、缓存、发题、备份与升级

### 缓存与数据迁移

- 首次加载页面与默认题库，之后复用本地题库和静态缓存；正常答题及管理无需服务器请求，浏览器仍可能发送少量版本检查请求。
- 不同设备、浏览器、域名、协议或端口独立保存。换域名、HTTP 改 HTTPS、换端口前，先从原地址导出备份，再在新地址恢复。
- 清理网站数据、无痕模式或空间回收可能影响记录；服务器备份不包含各浏览器的题库和成绩。
- “管理题库 → 备份全部数据”导出全部题库、错题、设置和进度 JSON；“恢复数据备份”会替换目标浏览器现有数据。

### 分发和更换题库

“导出 XML”可发给其他人导入，不含错题次数和进度。服务器没有共享题库管理 API，老师修改自己浏览器缓存不会更新所有学生。

更换首次访问的内置题库需修改 `web/dist/default.xml`；同步桌面版时也修改 `assets/default.xml`，再重编译 Go/桌面程序或重新打包静态前端。已有浏览器中的题库不会被新内置文件直接覆盖，已有用户应手动导入或编辑。

### Go 更新与回滚

Go 内嵌前端，必须替换程序并重启；修改它旁边的 HTML 不会生效。先下载并校验新附件，重命名为 `chooser`，上传到 `~/chooser-upload/chooser`：

```bash
sudo install -m 755 ~/chooser-upload/chooser /opt/chooser/chooser.next
sudo cp -p /opt/chooser/chooser /opt/chooser/chooser.previous
sudo systemctl stop chooser
sudo mv /opt/chooser/chooser.next /opt/chooser/chooser
sudo systemctl start chooser
curl -f http://127.0.0.1:8088/healthz
curl -f http://127.0.0.1:8088/api/version
```

`chooser.previous` 是本次更新前的程序，下次更新会覆盖；长期留存的版本另按版本号存档。子目录部署在检查 URL 加前缀。回滚本次更新：

```bash
sudo systemctl stop chooser
sudo install -m 755 /opt/chooser/chooser.previous /opt/chooser/chooser
sudo systemctl start chooser
curl -f http://127.0.0.1:8088/healthz
```

回滚程序不等于恢复浏览器数据，升级前仍需导出重要记录。Windows 更新先停止进程/计划任务，保留旧 EXE，再替换、重启。

### 静态前端更新

同一版本的文件一起上传。自行修改 JS/CSS 时同步更新 `index.html`、`help.html` 的资源查询版本和 `sw.js` 的 `VERSION`、`FILES`；不要长期缓存首页和 `sw.js`。修改默认 XML 时也需更新缓存策略，避免普通 HTTP 缓存复用旧文件。

更新后联网打开网页，关闭旧标签页再重开；必要时刷新以加载已激活的新缓存。旧页面先查程序版本与响应头，不要直接清空网站数据，以免删除题库与错题。

## 九、常见故障

| 现象 | 检查方法 |
| --- | --- |
| `Exec format error` | `uname -m` 查架构；Linux 使用 Linux 程序，ARM 另行编译 |
| `Permission denied` | 查 `chmod 755`、目录可访问、是否 `noexec` 挂载；systemd 使用 `/opt/chooser` 中程序 |
| `address already in use` | `sudo ss -ltnp 'sport = :8088'` 查占用，停旧测试进程或同时修改 Go/代理端口 |
| 本机能访问、手机打不开 | 手机不能填 `127.0.0.1`；查实际 IP、监听地址、安全组、防火墙和网络 |
| Nginx 502 | 先 `curl http://127.0.0.1:8088/healthz`，再查服务状态、日志和代理端口 |
| 子目录 404 / 样式丢失 | `-base-path` 与反代前缀一致，`proxy_pass` 保留前缀，从带末尾 `/` 的地址进入 |
| 证书申请失败 | 查 DNS A/AAAA、域名站点、80 端口和证书工具日志 |
| 手机不能完全离线打开 | 区分本地题库保存与页面缓存，长期使用受信任 HTTPS |
| 换地址后题库“消失” | 回原地址导出备份，在新地址恢复 |
| 更新后还是旧页面 | 查实际运行程序、版本接口、资源/SW 版本，关闭旧标签页重开 |
| 服务器没有学生成绩文件 | 当前成绩与记录保存在各自浏览器，无集中成绩数据库 |

## 十、Windows 桌面离线版

把 `chooser-desktop-v1.4.0.exe` 单独发给老师，放到 Windows 10/11 双击。原生 WPF 依赖 .NET Framework 4.8，无需浏览器组件、Python 或 Node.js，默认题库和帮助已内嵌。

缓存为 `%LOCALAPPDATA%\ChooserClassroom\state.json`，保存保留上一份 `.bak`。移动 EXE 不影响同一 Windows 用户缓存；`--data-dir <目录>` 可指定独立目录。详细操作和快捷键见 [使用说明](使用说明.md)。

## 十一、源码构建与验证

```bash
git clone https://github.com/zlight-GA106/chooser.git
cd chooser
```

### Go 网页服务端

构建需 Go 1.22 或更新版本，运行产物无需 Go，只依赖标准库。Linux/macOS shell：

```bash
cd web
go test ./...
CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -trimpath -ldflags '-s -w' -o ../chooser-linux-amd64 .
CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build -trimpath -ldflags '-s -w' -o ../chooser-windows-amd64.exe .
```

ARM64 Linux 使用 `GOOS=linux GOARCH=arm64`。Windows PowerShell 从仓库根目录运行 `.\web\build-go.ps1`，先测试再输出两种 v1.4 服务端；参数 `-Go 'Go可执行文件完整路径'` 可指定 SDK。修改 `web/dist/` 后需重新构建以嵌入修改，无前端生产依赖安装步骤。

### 桌面版及测试

具备 .NET Framework 4.8/WPF 编译组件的 Windows，从仓库根目录执行：

```powershell
.\build.ps1
```

脚本调用 Framework 的 `csc.exe`，输出 AnyCPU 单文件 EXE，无需 NuGet/.NET SDK。验证入口：

```powershell
.\tests\run-management-core.ps1
.\tests\desktop-v1.4-run-management.ps1
.\tests\desktop-run-options.ps1
.\tests\run-ui-tests.ps1
Start-Process -FilePath '.\选择器单文件版v1.4.exe' -ArgumentList '--self-test', 'test-report.txt' -Wait

# Node.js 仅用于开发测试
node web/tests/core-node-tests.js
node web/tests/storage-tests.js
```

原生浏览器验证页为 `web/tests/core-browser.html`、`web/tests/management-browser.html`，用开发静态 HTTP 服务提供 `web/` 后打开；发布程序不包含测试入口。可选 Node 管理测试需要开发依赖 `@xmldom/xmldom`，不属于生产依赖。Windows PowerShell：

```powershell
npm install --prefix "$env:TEMP/chooser-management-test-deps" @xmldom/xmldom
node web/tests/management-node-tests.js
```

v1.4 管理 C# 核心 8 组、JavaScript 核心 8 组、WPF 管理界面 10 组、Go HTTP 6 组通过，可通过上述入口复查。手机验证范围见本文开头；本地测试生成的报告、截图和缓存不随源码发布。

| 路径 | 内容 |
| --- | --- |
| `src/` | WPF 界面、XML 解析、判分、管理、持久化 |
| `assets/` | 桌面题库、图标、指南、示例 |
| `web/main.go` | Go HTTP(S) 服务端 |
| `web/dist/` | 完整静态前端 |
| `web/tests/`、`tests/` | 核心、界面、服务与手机验收 |
| `web/README-server.md` | 服务端参数补充说明 |

## 许可证

本项目遵循 [LICENSE](LICENSE) 中的 GPL-3.0。Go 许可证说明见 [web/LICENSE-Go.txt](web/LICENSE-Go.txt)。
