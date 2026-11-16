#!/usr/bin/env bash
# Oracle Cloud (Ubuntu) sunucusunu BİR KERE hazırlar: kullanıcı, klasörler, HTTPS (Caddy), güvenlik duvarı, servis, günlük yedek.
# Kullanım (sunucuda):  sudo bash sunucu-kur.sh ekip.duckdns.org
set -euo pipefail

ALAN="${1:?Kullanım: sudo bash sunucu-kur.sh senin-alan-adin.duckdns.org}"
KLASOR="$(cd "$(dirname "$0")" && pwd)"

echo "==> Kullanıcı ve klasörler"
id ekip &>/dev/null || useradd --system --home /opt/ekip --shell /usr/sbin/nologin ekip
mkdir -p /opt/ekip /var/lib/ekip
chown -R ekip:ekip /opt/ekip /var/lib/ekip

echo "==> Paketler (Caddy: HTTPS sertifikasını kendisi alır ve yeniler)"
apt-get update -y
apt-get install -y caddy sqlite3 netfilter-persistent

echo "==> Caddy: https://$ALAN -> localhost:5080"
cat > /etc/caddy/Caddyfile <<EOF
$ALAN {
    reverse_proxy localhost:5080
}
EOF
systemctl enable caddy
systemctl restart caddy

echo "==> Güvenlik duvarı: 80 ve 443 açılıyor (Oracle'ın Ubuntu imajı varsayılan olarak her şeyi kapatır)"
iptables -C INPUT -p tcp --dport 80 -j ACCEPT 2>/dev/null || iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
iptables -C INPUT -p tcp --dport 443 -j ACCEPT 2>/dev/null || iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
netfilter-persistent save

echo "==> Servis"
cp "$KLASOR/ekip-sunucu.service" /etc/systemd/system/ekip-sunucu.service
systemctl daemon-reload
systemctl enable ekip-sunucu

echo "==> Günlük yedek (her gece 04:00, son 7 gün saklanır: yedek-1.db ... yedek-7.db)"
cat > /etc/cron.d/ekip-yedek <<'EOF'
0 4 * * * ekip sqlite3 /var/lib/ekip/ekip.db ".backup '/var/lib/ekip/yedek-$(date +\%u).db'"
EOF

echo
echo "Hazır. Şimdi Windows'tan sunucu dosyalarını gönder:  dagitim\\yayinla.ps1 -Sunucu <IP>"
echo "Sonra kontrol: https://$ALAN  ->  'Ekip sunucusu çalışıyor.' yazmalı."
