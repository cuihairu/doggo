/** Access control: admins pass the "admin" gate; the token's role claim decides. */
export default function access(initialState: { isAdmin?: boolean } | undefined) {
  return {
    admin: initialState?.isAdmin === true,
  }
}
