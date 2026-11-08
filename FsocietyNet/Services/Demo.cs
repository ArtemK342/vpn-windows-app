namespace FsocietyNet.Services;

/// <summary>Fixtures for the preview build (`-render`, `-demo`): every screen without network or an account.</summary>
public static class Demo
{
    public static LoginResponse Login() => new() { access_token = "demo", refresh_token = "demo" };
    public static GenerateNumericResponse Numeric() => new() { code = "4821-0937-5512-6604", access_token = "demo", refresh_token = "demo" };
    public static UserResponse Me() => new() { id = "1", email = "user@example.com", role = "user" };
    public static SubscriptionResponse Subscription() => new() { is_active = true, plan = "month", expires_at = "2026-12-08T00:00:00" };
    public static UsageResponse Usage() => new() { is_limited = false, bytes_used = 1_850_000_000, limit_bytes = 0 };

    public static List<ServerResponse> Servers() =>
    [
        new() { id = "fi", name = "Финляндия — Хельсинки", name_en = "Finland — Helsinki", country = "Finland", is_active = true, connectable = true, server_type = "wireguard", endpoint = "1.1.1.1:51820" },
        new() { id = "ch", name = "Швейцария — Цуг", name_en = "Switzerland — Zug", country = "Switzerland", is_active = true, connectable = true, server_type = "wireguard", allow_auto_connect = false },
        new() { id = "de", name = "Германия — Эрльбах", name_en = "Germany — Erlbach", country = "Germany", is_active = true, connectable = true, server_type = "wireguard", status = "degraded", status_message = "high_load" },
        new() { id = "de-v", name = "Германия — Эрльбах", name_en = "Germany — Erlbach", country = "Germany", is_active = true, connectable = true, server_type = "vless", logs_enabled = true },
        new() { id = "de-t", name = "Германия — Эрльбах", name_en = "Germany — Erlbach", country = "Germany", is_active = true, connectable = true, server_type = "trojan", subscription_required = true },
        new() { id = "de-h", name = "Германия — Эрльбах", name_en = "Germany — Erlbach", country = "Germany", is_active = true, connectable = true, server_type = "hysteria", status = "testing" },
        new() { id = "de-o", name = "Германия — Эрльбах", name_en = "Germany — Erlbach", country = "Germany", is_active = true, connectable = true, server_type = "openvpn_cloak" },
        new() { id = "ru-b", name = "Обход (YouTube, Discord)", name_en = "Bypass (YouTube, Discord)", country = "Russia", is_active = true, connectable = true, server_type = "bypass" },
        new() { id = "nl", name = "Нидерланды — Амстердам", name_en = "Netherlands — Amsterdam", country = "Netherlands", is_active = false, connectable = false, status = "coming_soon" },
    ];

    public static readonly Dictionary<string, int> Pings = new() { ["fi"] = 38, ["ch"] = 61, ["de"] = 54, ["de-v"] = 55, ["de-t"] = 55, ["de-h"] = 57, ["de-o"] = 55 };

    public static AppConfigResponse AppConfig() => new()
    {
        access = new AccessStatus { maintenance = new MaintenanceStatus { enabled = false }, registration = new() { ["email"] = true, ["anonymous"] = true } }
    };

    public static NewsPage News() => new()
    {
        items =
        [
            new() { id = "1", title = "FsocietyNet для Windows", title_en = "FsocietyNet for Windows",
                    body = "Новое приложение для компьютера: **AmneziaWG**, VLESS, Trojan, Hysteria2 и «Режим РФ», правила туннелирования, Kill Switch и тёмная/светлая тема.\n\nСкачать — на сайте, раздел «Скачать».",
                    body_en = "A new desktop app: **AmneziaWG**, VLESS, Trojan, Hysteria2 and «RF mode», split tunnelling, kill switch and dark/light themes.",
                    published_at = "2026-11-09T09:00:00" },
            new() { id = "2", title = "Строки для сторонних приложений", title_en = "Lines for other apps",
                    body = "С подпиской можно получить строку для AmneziaVPN и ссылку-подписку для Happ, v2rayNG и Hiddify: правый клик по серверу → «Для другого приложения».",
                    published_at = "2026-11-08T12:00:00" },
        ],
        total = 2
    };

    public static List<TicketResponse> Tickets() =>
    [
        new() { id = "t1", subject = "Не подключается Hysteria2", status = "open", created_at = "2026-11-07T10:00:00", updated_at = "2026-11-08T09:12:00", unread = 1 },
        new() { id = "t2", subject = "Вопрос по оплате", status = "closed", created_at = "2026-10-20T10:00:00", updated_at = "2026-10-21T15:00:00" },
    ];

    public static TicketDetail Ticket(string id) => new()
    {
        ticket = Tickets().FirstOrDefault(t => t.id == id) ?? Tickets()[0],
        messages =
        [
            new() { sender = "user", message = "Добрый день! Hysteria2 подключается, но через минуту пропадает интернет.", created_at = "2026-11-07T10:00:00" },
            new() { sender = "admin", message = "Здравствуйте! Попробуйте в настройках протоколов включить BBR и переподключиться. Если не поможет — включите журнал и пришлите его сюда.", created_at = "2026-11-08T09:12:00" },
        ]
    };

    public static ExternalOverview External() => new()
    {
        entitled = true, slots = new() { used = 1, max = 3 },
        awg_servers = [new() { id = "fi", name = "Финляндия — Хельсинки" }, new() { id = "ch", name = "Швейцария — Цуг" }, new() { id = "de", name = "Германия — Эрльбах" }],
        vless_servers = [new() { id = "de-v", name = "Германия — Эрльбах" }],
        sub_url = "https://fsociety-vpn.org/api/sub/xxxxxxxx",
        vless_lines = new() { ["de-v"] = "vless://…#Germany" },
    };
}
