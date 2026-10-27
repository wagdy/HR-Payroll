import { A11yModule } from '@angular/cdk/a11y';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, ElementRef, Injector, afterNextRender, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { HrAccess } from '../../core/hr/hr-access';
import { hrTabs } from '../../core/hr/hr-tabs';
import { CorrectionsCount } from './corrections-count';
import { PendingLeaves } from './pending-leaves';

// Narrower than this, the sections are a drawer behind a menu button, above the page (as the
// styles' own media query says).
const Compact = '(max-width: 959.98px)';

// HR & Payroll (الموارد البشرية والرواتب), opening on who is in now: the employees and their
// departments, their attendance and its corrections, the monthly payroll, the leaves and the
// public holidays, and the attendance devices that send their punches. The Corrections tab, and
// Leaves & Holidays, count what waits there for a manager. Each tab for whoever holds it (صلاحيات الموظفين): a manager every one, and the
// Permissions tab where they give them; an accountant, and an employee in the portal, those a
// manager gave them, their changes going to a manager.
// Advances are paid in Financials and in the daily batch, from the safe or a day's takings; the
// Advances tab says whose pay each comes off, and gives out those handed out with no one named.
//
// The tabs are a sidebar at the start of the page, the page beside it: on the left, or on the
// right for right-to-left (its sides are logical, start and end). On a phone they are a drawer,
// opened from a bar above the page that names the tab open; picking one, Escape or the backdrop
// closes it.
@Component({
  selector: 'app-hr-shell',
  imports: [A11yModule, MatButtonModule, MatIconModule, RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="hr-layout">
      <div class="mobile-bar no-print">
        <button
          #menuButton
          mat-icon-button
          class="menu"
          (click)="openMenu()"
          aria-controls="hr-sections"
          [attr.aria-expanded]="menuOpen()"
          aria-label="HR & Payroll sections"
        >
          <mat-icon>menu</mat-icon>
          @if (waiting.count() || leaves.count()) {
            <span class="dot" aria-hidden="true"></span>
          }
        </button>
        @if (current(); as tab) {
          <mat-icon class="current-icon">{{ tab.icon }}</mat-icon>
          <span class="current">{{ tab.en }}<span class="inline-ar">{{ tab.ar }}</span></span>
        } @else {
          <span class="current">HR &amp; Payroll<span class="inline-ar">الموارد البشرية والرواتب</span></span>
        }
      </div>

      @if (menuOpen()) {
        <div class="backdrop no-print" (click)="closeMenu(true)" aria-hidden="true"></div>
      }

      <aside
        #side
        id="hr-sections"
        class="side no-print"
        [class.open]="menuOpen()"
        [cdkTrapFocus]="menuOpen()"
        [attr.role]="menuOpen() ? 'dialog' : null"
        [attr.aria-modal]="menuOpen() || null"
        aria-labelledby="hr-sections-title"
        (keydown.escape)="closeMenu(true)"
      >
        <header class="side-head">
          <h1 id="hr-sections-title">HR &amp; Payroll <span class="sub">الموارد البشرية والرواتب</span></h1>
          <button mat-icon-button class="close" (click)="closeMenu(true)" aria-label="Close the sections"><mat-icon>close</mat-icon></button>
        </header>
        <nav aria-label="HR & Payroll sections">
          @for (tab of tabs(); track tab.path) {
            <a class="side-link" [routerLink]="tab.path" routerLinkActive="active" ariaCurrentWhenActive="page">
              <mat-icon>{{ tab.icon }}</mat-icon>
              <span class="label"><span class="en">{{ tab.en }}</span><span class="sub">{{ tab.ar }}</span></span>
              @if (tab.path === 'corrections' && waiting.count()) {
                <span class="count" [attr.aria-label]="waiting.count() + ' waiting'">{{ waiting.count() }}</span>
              }
              @if (tab.path === 'leaves' && leaves.count()) {
                <span class="count" [attr.aria-label]="leaves.count() + ' leave requests waiting'">{{ leaves.count() }}</span>
              }
            </a>
          }
        </nav>
      </aside>

      <div class="body"><router-outlet /></div>
    </div>
  `,
  styleUrl: '../shared/pages.scss',
  styles: `
    // The shell's sticky bar above this (--app-sticky-top, none in the portal): the sidebar and the
    // phone's bar stay in view under it.
    .hr-layout {
      display: grid;
      grid-template-columns: 256px minmax(0, 1fr);
      gap: 20px;
      align-items: start;
    }
    .body {
      min-width: 0;
    }
    .mobile-bar,
    .close {
      display: none;
    }

    .side {
      position: sticky;
      top: calc(var(--app-sticky-top, 0px) + 16px);
      max-height: calc(100dvh - var(--app-sticky-top, 0px) - 32px);
      overflow-y: auto;
      overscroll-behavior: contain;
      padding: 12px;
      box-sizing: border-box;
      border-radius: 20px;
      background: var(--mat-sys-surface-container-lowest);
      border: 1px solid color-mix(in srgb, var(--mat-sys-outline-variant) 70%, transparent);
      box-shadow:
        0 1px 2px rgb(15 23 42 / 4%),
        0 10px 28px -14px rgb(15 23 42 / 18%);
    }
    .side-head {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      padding: 6px 10px 14px;
      margin-bottom: 8px;
      border-bottom: 1px solid color-mix(in srgb, var(--mat-sys-outline-variant) 70%, transparent);

      h1 {
        display: grid;
        gap: 2px;
        margin: 0;
        font: var(--mat-sys-title-medium);
        font-weight: 650;
        letter-spacing: -0.01em;
      }
    }
    nav {
      display: grid;
      gap: 4px;
    }

    // A section: its icon, its name over its Arabic, and what waits there at the end. The one
    // open is tinted, with a bar of the brand's colour on its start side.
    .side-link {
      position: relative;
      display: flex;
      align-items: center;
      gap: 12px;
      min-height: 48px;
      padding: 8px 12px;
      border-radius: 12px;
      color: var(--mat-sys-on-surface-variant);
      text-decoration: none;
      transition:
        background-color 150ms ease,
        color 150ms ease;

      mat-icon {
        flex: none;
        transition: transform 150ms ease;
      }
      &:hover {
        background: var(--mat-sys-surface-container);
        color: var(--mat-sys-on-surface);

        mat-icon {
          transform: scale(1.06);
        }
      }
      &:focus-visible {
        outline: 2px solid var(--mat-sys-primary);
        outline-offset: 1px;
      }
      &.active {
        background: color-mix(in srgb, var(--mat-sys-primary) 10%, transparent);
        color: var(--mat-sys-primary);

        .en {
          font-weight: 650;
        }
        &::before {
          content: '';
          position: absolute;
          inset-inline-start: 0;
          inset-block: 10px;
          width: 4px;
          border-start-end-radius: 4px;
          border-end-end-radius: 4px;
          background: var(--mat-sys-primary);
        }
      }
    }
    .label {
      display: grid;
      min-width: 0;
      line-height: 1.25;

      .en {
        font: var(--mat-sys-label-large);
        font-size: 14.5px;
      }
    }
    // The Arabic under the English, on the same side as the page's text: isolated, not turned
    // right-to-left, which would push it to the far side.
    .sub {
      unicode-bidi: isolate;
      font: var(--mat-sys-body-small);
      opacity: 0.75;
    }
    .count {
      margin-inline-start: auto;
      min-width: 22px;
      padding: 0 7px;
      box-sizing: border-box;
      border-radius: 999px;
      font: var(--mat-sys-label-small);
      font-weight: 700;
      line-height: 22px;
      text-align: center;
      background: var(--app-warn);
      color: #000;
    }

    @media (max-width: 959.98px) {
      .hr-layout {
        display: block;
      }

      // The tab open, under the shell's bar; the menu button opens the rest.
      .mobile-bar {
        position: sticky;
        top: var(--app-sticky-top, 0px);
        z-index: 5;
        display: flex;
        align-items: center;
        gap: 8px;
        margin: 0 0 12px;
        padding-block: 4px;
        padding-inline: 4px 14px;
        border-radius: 16px;
        background: color-mix(in srgb, var(--mat-sys-surface-container-lowest) 90%, transparent);
        backdrop-filter: blur(10px);
        border: 1px solid color-mix(in srgb, var(--mat-sys-outline-variant) 70%, transparent);
        box-shadow: 0 6px 18px -12px rgb(15 23 42 / 25%);
      }
      .menu {
        position: relative;
      }
      .dot {
        position: absolute;
        top: 9px;
        inset-inline-end: 9px;
        width: 9px;
        height: 9px;
        border-radius: 50%;
        background: var(--app-warn);
        box-shadow: 0 0 0 2px var(--mat-sys-surface-container-lowest);
      }
      .current-icon {
        color: var(--mat-sys-primary);
      }
      .current {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font: var(--mat-sys-title-small);
      }

      .backdrop {
        position: fixed;
        inset: 0;
        z-index: 40;
        background: rgb(15 23 42 / 42%);
        animation: hr-fade 200ms ease;
      }

      // A drawer from the start side, over everything; hidden, it is not reached by Tab either.
      .side {
        position: fixed;
        top: 0;
        bottom: 0;
        inset-inline-start: calc(-1 * min(304px, 86vw) - 24px);
        z-index: 41;
        width: min(304px, 86vw);
        max-height: none;
        border-radius: 0;
        border-start-end-radius: 24px;
        border-end-end-radius: 24px;
        box-shadow: 0 24px 48px -12px rgb(15 23 42 / 35%);
        visibility: hidden;
        transition:
          inset-inline-start 240ms cubic-bezier(0.2, 0.8, 0.2, 1),
          visibility 0s linear 240ms;

        &.open {
          inset-inline-start: 0;
          visibility: visible;
          transition: inset-inline-start 240ms cubic-bezier(0.2, 0.8, 0.2, 1);
        }
      }
      .close {
        display: inline-flex;
        margin-block: -6px;
        margin-inline: auto -6px;
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .side,
      .side.open,
      .side-link,
      .side-link mat-icon,
      .backdrop {
        transition: none;
        animation: none;
      }
    }

    @keyframes hr-fade {
      from {
        opacity: 0;
      }
    }

    @media print {
      .hr-layout {
        display: block;
      }
    }
  `,
})
export class HrShell {
  protected readonly waiting = inject(CorrectionsCount);
  protected readonly leaves = inject(PendingLeaves);
  private readonly access = inject(HrAccess);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);
  protected readonly tabs = computed(() => [
    ...hrTabs.filter((t) => this.access.has(t.tab)),
    ...(this.access.canGive() ? [{ path: 'permissions', en: 'Permissions', ar: 'صلاحيات الموظفين', icon: 'admin_panel_settings' }] : []),
  ]);

  protected readonly menuOpen = signal(false);
  private readonly compact = toSignal(
    inject(BreakpointObserver)
      .observe(Compact)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );
  // The tab open, named on the phone's bar.
  protected readonly current = computed(() => {
    this.url();
    return this.tabs().find((tab) =>
      this.router.isActive(this.router.createUrlTree([tab.path], { relativeTo: this.route }), {
        paths: 'subset',
        queryParams: 'ignored',
        fragment: 'ignored',
        matrixParams: 'ignored',
      }),
    );
  });

  private readonly side = viewChild.required<ElementRef<HTMLElement>>('side');
  private readonly menuButton = viewChild.required<unknown, ElementRef<HTMLElement>>('menuButton', { read: ElementRef });

  constructor() {
    void this.waiting.refresh();
    void this.leaves.refresh();
    // A tab picked, or the screen grown wide enough for the sidebar: the drawer closes.
    effect(() => {
      this.url();
      this.compact();
      untracked(() => this.menuOpen.set(false));
    });
  }

  // Open, the tab open has the focus (or the first), and Tab stays in the drawer.
  protected openMenu(): void {
    this.menuOpen.set(true);
    afterNextRender(
      () => {
        const side = this.side().nativeElement;
        (side.querySelector<HTMLElement>('a.active') ?? side.querySelector<HTMLElement>('a'))?.focus();
      },
      { injector: this.injector },
    );
  }

  protected closeMenu(returnFocus = false): void {
    if (!this.menuOpen()) {
      return;
    }
    this.menuOpen.set(false);
    if (returnFocus) {
      this.menuButton().nativeElement.focus();
    }
  }
}
