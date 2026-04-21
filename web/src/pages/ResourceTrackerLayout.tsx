import { NavLink, Outlet } from 'react-router-dom'
import type { MeProfile } from '../api'
import '../App.css'

export type ResourceTrackerLayoutSession = { token: string; profile: MeProfile }

export default function ResourceTrackerLayout({ session }: { session: ResourceTrackerLayoutSession }) {
  const role = session.profile.role
  const showTaskBoard = role === 'Admin' || role === 'Manager' || role === 'Partner'

  return (
    <div className="resource-tracker-suite">
      <div className="card admin-card resource-tracker-suite-tabs-wrap">
        <nav className="resource-tracker-suite-tabs" aria-label="Resource Tracker sections">
          <NavLink
            to="/resource-tracker"
            end
            className={({ isActive }) => `resource-tracker-suite-tab${isActive ? ' active' : ''}`}
          >
            People &amp; Availability
          </NavLink>
          {showTaskBoard ? (
            <NavLink
              to="/resource-tracker/project-tasks"
              className={({ isActive }) => `resource-tracker-suite-tab${isActive ? ' active' : ''}`}
            >
              Project Task Board
            </NavLink>
          ) : null}
        </nav>
      </div>
      <Outlet context={session} />
    </div>
  )
}
