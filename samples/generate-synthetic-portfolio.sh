#!/usr/bin/env bash
# File purpose: Regenerates five complex, token-free synthetic tenant stylesheets for repeatable migration analysis.
set -euo pipefail

sample_dir="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$sample_dir/tenants"

generate_tenant() {
  local slug="$1" brand="$2" accent="$3" ink="$4" surface="$5" page="$6" muted="$7" border="$8" font="$9"
  cat > "$sample_dir/tenants/$slug.css" <<CSS
/* Synthetic whole-application tenant override. No production data and no CSS custom properties. */
html { color: $ink; background: $page; font-family: $font, Arial, sans-serif; font-size: 16px; line-height: 1.5; }
body, .app-shell { margin: 0; color: $ink; background: $page; min-height: 100vh; }
.app-shell a { color: $brand; text-decoration-color: $accent; text-underline-offset: 3px; }
.app-header { color: #ffffff; background: $brand; min-height: 68px; padding: 14px 20px; box-shadow: 0 2px 12px rgba(0,0,0,.18); }
.app-header__logo { width: 132px; height: 36px; object-fit: contain; }
.app-header__title { color: #ffffff; font-size: 22px; font-weight: 800; line-height: 1.1; letter-spacing: .02em; }
.app-header__subtitle { color: rgba(255,255,255,.72); font-size: 13px; margin-top: 3px; }
.app-header__action { color: #ffffff; background: rgba(255,255,255,.14); border-color: rgba(255,255,255,.38); border-radius: 10px; padding: 9px 12px; }
.mobile-nav { color: $muted; background: $surface; padding: 8px 12px; gap: 5px; box-shadow: 0 -3px 14px rgba(0,0,0,.12); display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); }
.mobile-nav__item { color: $muted; padding: 8px 5px; font-size: 11px; font-weight: 700; line-height: 1.2; border-radius: 10px; }
.mobile-nav__item.is-active { color: $brand; background: $accent; border-radius: 10px; font-weight: 800; }
.side-menu { color: #ffffff; background: $ink; width: 320px; padding: 28px 22px; box-shadow: 14px 0 40px rgba(0,0,0,.22); }
.side-menu__item { color: rgba(255,255,255,.84); padding: 12px 8px; margin-bottom: 4px; font-size: 17px; border-radius: 8px; }
.side-menu__item.is-selected { color: #ffffff; background: $brand; font-weight: 800; }
.page-heading { color: $ink; font-size: 30px; font-weight: 850; line-height: 1.05; letter-spacing: -.02em; margin: 24px 0 14px; }
.section-heading { color: $ink; font-size: 21px; font-weight: 800; line-height: 1.2; margin: 22px 0 12px; }
.section-kicker { color: $brand; font-size: 12px; font-weight: 800; line-height: 1.3; letter-spacing: .12em; text-transform: uppercase; }
.content-card, .feed-item, .calendar-card, .academy-card { color: $ink; background: $surface; border-color: $border; border-radius: 14px; padding: 18px; margin-bottom: 16px; box-shadow: 0 4px 18px rgba(20,25,30,.09); }
.content-card__title, .feed-item__title, .calendar-card__title, .academy-card__title { color: $ink; font-size: 19px; font-weight: 800; line-height: 1.25; margin-bottom: 8px; }
.content-card__meta, .feed-item__meta, .calendar-card__meta, .academy-card__meta { color: $muted; font-size: 13px; line-height: 1.35; margin-bottom: 10px; }
.content-card__body, .feed-item__body { color: $ink; font-size: 16px; line-height: 1.58; }
.feed-item__author { color: $ink; font-size: 15px; font-weight: 800; }
.feed-item__department { color: $muted; font-size: 12px; margin-top: 2px; }
.feed-item__image { width: 100%; max-height: 520px; object-fit: cover; border-radius: 10px; margin: 14px 0; }
.feed-item__actions { color: $muted; border-color: $border; padding-top: 12px; margin-top: 14px; gap: 20px; }
.feed-item__reaction.is-selected { color: $brand; background: $accent; border-radius: 999px; padding: 5px 9px; }
.primary-button, .primary-cta { color: #ffffff; background: $brand; border-color: $brand; border-radius: 9px; padding: 12px 18px; font-size: 15px; font-weight: 800; box-shadow: 0 3px 10px rgba(0,0,0,.16); }
.primary-button:hover, .primary-cta:hover { color: #ffffff; background: $ink; border-color: $ink; box-shadow: 0 5px 16px rgba(0,0,0,.22); }
.secondary-button, .secondary-cta { color: $brand; background: $surface; border-color: $brand; border-radius: 9px; padding: 12px 18px; font-size: 15px; font-weight: 800; }
.quiet-button { color: $muted; background: transparent; border-color: $border; border-radius: 8px; padding: 9px 13px; font-size: 14px; font-weight: 700; }
.danger-button { color: #ffffff; background: #b42318; border-color: #b42318; border-radius: 9px; padding: 12px 18px; font-weight: 800; }
.form-field label { color: $ink; font-size: 13px; font-weight: 800; margin-bottom: 6px; }
.form-field input, .form-field textarea, .form-field select { color: $ink; background: $surface; border-color: $border; border-radius: 9px; padding: 12px 13px; font-size: 16px; box-shadow: inset 0 1px 2px rgba(0,0,0,.04); }
.form-field input:focus, .form-field textarea:focus, .form-field select:focus { border-color: $brand; outline-color: $accent; box-shadow: 0 0 0 3px rgba(20,40,60,.13); }
.form-field__help { color: $muted; font-size: 12px; line-height: 1.4; margin-top: 5px; }
.form-field.has-error input { color: #8a1c13; background: #fff4f2; border-color: #d92d20; }
.search-field { color: $ink; background: $surface; border-color: $border; border-radius: 999px; padding: 10px 16px; box-shadow: 0 2px 8px rgba(0,0,0,.06); }
.profile-hero { color: $ink; background: $accent; padding: 32px 20px 24px; text-align: center; }
.profile-avatar { width: 148px; height: 148px; border-color: $surface; border-radius: 50%; box-shadow: 0 6px 20px rgba(0,0,0,.22); }
.profile-heading { color: $ink; font-size: 32px; font-weight: 900; line-height: 1.05; margin: 16px 0 4px; }
.profile-role { color: $muted; font-size: 16px; line-height: 1.35; }
.profile-stat { color: $ink; background: $surface; padding: 16px 10px; text-align: center; border-color: $border; }
.profile-stat__value { color: $brand; font-size: 24px; font-weight: 900; line-height: 1; }
.profile-stat__label { color: $muted; font-size: 12px; margin-top: 5px; text-transform: uppercase; }
.academy-card__progress { color: $brand; background: $accent; border-radius: 999px; height: 9px; margin-top: 16px; }
.academy-card__lesson-count, .academy-card__duration { color: $muted; font-size: 13px; font-weight: 700; }
.achievement-badge { color: $brand; background: $accent; border-color: $brand; border-radius: 50%; padding: 14px; box-shadow: 0 3px 12px rgba(0,0,0,.12); }
.calendar-month { color: $ink; font-size: 25px; font-weight: 900; margin: 28px 0 14px; }
.calendar-card__date { color: $brand; font-size: 13px; font-weight: 800; letter-spacing: .02em; }
.calendar-card__attendees { color: $muted; background: $page; border-radius: 999px; padding: 4px 8px; font-size: 12px; }
.event-status { color: $brand; background: $accent; border-radius: 8px; padding: 8px 12px; font-weight: 800; }
.chat-empty { color: $muted; background: $surface; padding: 80px 24px; text-align: center; border-radius: 14px; }
.chat-bubble--self { color: #ffffff; background: $brand; border-radius: 18px 18px 4px 18px; padding: 11px 14px; box-shadow: 0 2px 8px rgba(0,0,0,.10); }
.chat-bubble--other { color: $ink; background: $surface; border-color: $border; border-radius: 18px 18px 18px 4px; padding: 11px 14px; }
.notification-badge { color: #ffffff; background: #e23b2d; border-color: $surface; border-radius: 999px; min-width: 22px; padding: 3px 6px; font-size: 11px; font-weight: 900; }
.survey-option { color: $ink; background: $surface; border-color: $border; border-radius: 10px; padding: 13px 15px; margin-bottom: 8px; }
.survey-option.is-selected { color: $brand; background: $accent; border-color: $brand; font-weight: 800; }
.task-row { color: $ink; background: $surface; border-color: $border; border-radius: 10px; padding: 14px 16px; margin-bottom: 9px; }
.task-row.is-complete { color: $muted; background: $page; text-decoration-color: $muted; opacity: .75; }
.leaderboard-row { color: $ink; background: $surface; border-color: $border; padding: 12px 15px; gap: 12px; }
.leaderboard-row.is-current { color: $brand; background: $accent; font-weight: 900; box-shadow: inset 4px 0 0 $brand; }
.modal-sheet, .dialog-panel { color: $ink; background: $surface; border-radius: 20px; padding: 26px; box-shadow: 0 24px 72px rgba(0,0,0,.30); }
.modal-sheet__title, .dialog-panel__title { color: $ink; font-size: 23px; font-weight: 900; margin-bottom: 10px; }
.modal-backdrop { background: rgba(10,18,24,.64); backdrop-filter: blur(3px); }
.toast { color: #ffffff; background: $ink; border-radius: 10px; padding: 12px 16px; box-shadow: 0 10px 30px rgba(0,0,0,.24); }
.data-table { color: $ink; background: $surface; border-color: $border; border-radius: 12px; box-shadow: 0 3px 14px rgba(0,0,0,.07); }
.data-table th { color: $muted; background: $page; padding: 11px 13px; font-size: 12px; text-transform: uppercase; letter-spacing: .05em; }
.data-table td { color: $ink; border-color: $border; padding: 13px; }
.segmented-control { color: $muted; background: $page; border-radius: 10px; padding: 4px; gap: 4px; }
.segmented-control__item.is-active { color: #ffffff; background: $brand; border-radius: 7px; padding: 8px 12px; font-weight: 800; }
.floating-action { color: #ffffff; background: $brand; border-radius: 50%; width: 58px; height: 58px; right: 18px; bottom: 86px; box-shadow: 0 8px 22px rgba(0,0,0,.25); }
.skeleton-line { background: linear-gradient(90deg, $page 20%, $border 50%, $page 80%); border-radius: 6px; height: 12px; }
.divider { background: $border; height: 1px; margin: 18px 0; }
@media (max-width: 720px) { .page-heading { font-size: 26px; margin: 18px 0 12px; } .content-card, .feed-item, .calendar-card, .academy-card { padding: 14px; margin-bottom: 12px; } .side-menu { width: 86vw; } .data-table th, .data-table td { padding: 9px; } }
@media (min-width: 1100px) { .app-content { max-width: 1160px; margin: 0 auto; padding: 28px; } .content-grid { display: grid; grid-template-columns: minmax(0, 2fr) minmax(280px, 1fr); gap: 24px; } }
CSS

  case "$slug" in
    north) cat >> "$sample_dir/tenants/$slug.css" <<'CSS'
.store-promotion { color: #102a43; background: #ffdd66; padding: 24px; border-radius: 2px 28px 2px 28px; transform: translateX(6px); }
.store-promotion__price { font-size: 44px; font-weight: 950; letter-spacing: -.04em; }
.home-tile:nth-child(2) { margin-top: -18px; z-index: 4; }
CSS
      ;;
    harbour) cat >> "$sample_dir/tenants/$slug.css" <<'CSS'
.hero-campaign { min-height: 460px; background-image: linear-gradient(rgba(0,0,0,.04), rgba(0,0,0,.62)), url('/synthetic/harbour-hero.jpg'); background-size: cover; background-position: 42% center; }
.booking-widget { display: grid; grid-template-columns: 1fr 1fr auto; gap: 9px; position: sticky; top: 12px; }
.menu-card img { aspect-ratio: 4 / 3; object-fit: cover; filter: saturate(.86) contrast(1.04); }
CSS
      ;;
    field) cat >> "$sample_dir/tenants/$slug.css" <<'CSS'
.campaign-card { position: fixed; right: 3px; bottom: 12px; width: calc(100% - 24px); z-index: 9999; }
.task-board { display: grid; grid-template-columns: 280px minmax(0, 1fr); gap: 20px; }
.route-status[data-state='late'] { color: #ffffff !important; background: #a22b20 !important; animation: pulse-warning 1.2s infinite; }
@keyframes pulse-warning { 0%, 100% { opacity: 1; } 50% { opacity: .58; } }
CSS
      ;;
    studio) cat >> "$sample_dir/tenants/$slug.css" <<'CSS'
.client-special-shape { clip-path: polygon(0 0, 100% 8px, 100% 100%, 0 100%); transform: rotate(-.35deg); }
.home-campaign:nth-child(3) { margin-left: -22px !important; width: calc(100% + 44px); mix-blend-mode: multiply; }
.profile-heading, .page-heading { text-transform: uppercase; font-stretch: condensed; }
CSS
      ;;
    campus) cat >> "$sample_dir/tenants/$slug.css" <<'CSS'
.course-timeline { position: relative; padding-left: 64px; }
.course-timeline::before { content: ''; position: absolute; left: 31px; top: 0; bottom: 0; width: 2px; background: #d8c7e2; }
.quiz-result[data-score='perfect'] { color: #ffffff; background: #4f1370; filter: drop-shadow(0 8px 16px rgba(79,19,112,.24)); }
@import url('https://example.invalid/synthetic-font.css');
CSS
      ;;
  esac
}

generate_tenant north '#0057b8' '#e7f1fc' '#17212b' '#ffffff' '#f3f6f9' '#667788' '#d6dde5' 'Inter'
generate_tenant harbour '#174b3f' '#e4eee8' '#18312b' '#fffdf8' '#f7f3ed' '#6f7b77' '#ddd4c8' 'Source Sans 3'
generate_tenant field '#d5532f' '#fbe8e2' '#20252a' '#ffffff' '#f2f3f4' '#687078' '#d5d8da' 'Roboto'
generate_tenant studio '#7257ff' '#eee7ff' '#15131c' '#ffffff' '#f8f3ff' '#746d7b' '#e2d8ec' 'DM Sans'
generate_tenant campus '#6b2d8f' '#f0e5f6' '#252038' '#ffffff' '#f6f5fa' '#716b7a' '#ddd8e5' 'Nunito'

printf 'Generated five synthetic, token-free tenant stylesheets in %s/tenants\n' "$sample_dir"
