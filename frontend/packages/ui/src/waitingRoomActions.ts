/** Whether the waiting-room Leave control should be shown. Admins cannot leave upstream. */
export function canLeaveWaitingRoom(isAdmin: boolean): boolean {
  return !isAdmin;
}
