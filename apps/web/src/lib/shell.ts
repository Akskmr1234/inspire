/**
 * What the application's frame and the screens inside it say to each other, as window
 * events: a screen covering the frame's own menu button asks it to open the menu, and
 * the frame says when somebody picks somewhere to go from it - which is a bill's cue to
 * put itself on hold before the screen it is on goes away.
 */

/** Asks the shell to open its navigation drawer. */
export const OPEN_MENU_EVENT = 'erp:open-menu';

/** Said by the shell when a destination is picked from its menu. */
export const MENU_CHOSEN_EVENT = 'erp:menu-chosen';

/** Opens the navigation, from a window that covers the shell's own button. */
export function openMenu(): void {
  window.dispatchEvent(new Event(OPEN_MENU_EVENT));
}

/** Tells whatever is listening that the menu was used to go somewhere. */
export function menuChosen(): void {
  window.dispatchEvent(new Event(MENU_CHOSEN_EVENT));
}
