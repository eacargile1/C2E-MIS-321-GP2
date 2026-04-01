# Client System Requirements – Unified Business Operations Platform

## Overview

The client currently runs their business operations using two separate systems:

**A Harvest-like internal system for:**
- Time tracking
- Expense tracking
- Project management
- Reporting
- Client management
- Invoice generation

**A large Excel-based resource planning system called Resource Tracker used for:**
- Employee scheduling
- Staffing forecasting
- Project staffing needs

The client wants ONE unified system that integrates both systems and automates the connections between them.

The goal is to remove manual cross-referencing between multiple tools and automatically synchronize staffing, time tracking, budgeting, and project management.

---

## PART 1 — Internal Operations System (Harvest-style)

### 1. Time Tracking (Timesheet Page)

Every week employees must enter their work activity.

**Purpose:**
- Track who is working
- Track what they are working on
- Track where time is being spent
- Determine billable vs non-billable time
- Support client invoicing
- Ensure employees get paid

Each employee submits weekly timesheets.

**Each entry must contain:**
- Employee
- Project
- Client
- Task description
- Date
- Hours worked
- Billable vs Non-Billable flag
- Notes (optional)

These entries are later used to:
- Generate invoices
- Track project budgets
- Analyze utilization

### 2. Expense Tracking

Employees must be able to submit work-related expenses.

The system should include a **Track Expense** button.

**Submitting an expense opens a form with:**
- Employee
- Expense date
- Expense amount
- Expense category
- Project (optional)
- Client (optional)
- Description
- Receipt upload (file attachment)

The expense submission will later be used for:
- Reimbursement
- Project budget tracking
- Reporting
- Invoicing support

### 3. Projects Tab

**The Projects page should allow users to:**
- Create projects
- Assign team members
- View team members on each project
- Track time spent on projects
- Track expenses related to projects
- Track project budgets

**Budget tracking should include:**
- Total hours logged by team members
- Expenses related to the project
- Comparison against budget

### 4. Reports Tab

**Users should be able to see:**

*Personal reporting (for themselves):*
- Total hours worked
- Billable hours
- Non-billable hours

*Client reporting (for a specific client):*
- Total hours across the whole team
- Billable amounts
- Time breakdown by employee
- Project costs

**Reports should include filters:**
- Client
- Project
- Employee
- Date range

### 5. Client Management (Manage Tab)

**Features:**
- List all clients
- Search clients
- Filter clients
- Add new clients

**Each client record should contain:**
- Client name
- Contact information
- General client information
- Related projects

Client entries should be expandable to view details.

### 6. Roles and Permissions

**Admin Role** — Full system access:
- Configure system settings
- Manage clients
- Manage projects
- Manage users
- Generate invoices

**Manager Role** — Team oversight:
- Assign team members to projects
- Assign tasks
- View individual contributor activity
- View reporting for their teams

**Finance Role** — Financial operations:
- Generate invoices
- Export invoices
- Process financial records
- Push invoice data into accounting systems
- NOTE: Payment processing does NOT happen in this system

**Individual Contributors** — Normal employees:
- Submit timesheets
- Log expenses
- See their assigned projects
- See their own reporting

### System Output

The system should generate client invoices using:
- Billable hours
- Project rates
- Reimbursable expenses

Invoices can then be sent to clients externally.

---

## PART 2 — Resource Tracker (Excel System Replacement)

### Sheet 1 — Availability

Calendar-style grid showing staff availability.

**Each employee row contains:**
- Employee Name
- Job Title
- Daily availability status

**Color coding:**
- 🔵 Blue = Fully Booked
- 🟢 Green = Soft Booked
- 🔴 Red = Available
- 🟡 Yellow = PTO

Once a Statement of Work (SOW) is signed, employee status changes from Soft Booked → Fully Booked.

Used for staffing forecasts.

### Sheet 2 — Needs

Tracks upcoming project staffing needs. Rows grouped by client.

**Each entry contains:**
- Client name
- Role level required
- Sales stage (Won / Negotiation / Commit / other pipeline stages)
- Start date
- End date
- Assigned employee (specific person, "TBD", or "OPEN")
- Required technical skills (e.g., C#, .NET, Data Engineering, Data Modeling)
- Job requirements
- Project notes

---

## Core Problem

Currently these systems are completely separate. Employees must manually cross-reference timesheets, resource tracker, staffing needs, PTO, and project assignments — creating significant inefficiencies.

---

## Client Goal — Unified System with Automations

### PTO Integration
When PTO is approved → automatically update staffing calendar to Yellow (PTO). Ensures forecasting doesn't assume billable hours on PTO days.

### Project Assignment Automation
When someone is assigned to a project → the system could automatically populate their timesheet. If expected to bill 40 hours, auto-generate timesheet entries that employees can then edit.

### Resource Tracker Automation
- When a person is tagged on a Need entry → availability changes to Green (Soft Booked)
- When the deal closes and project starts → status changes to Blue (Fully Booked)

### Desired Outcome

A single integrated platform where the following are all linked automatically:
- Project staffing
- PTO
- Timesheets
- Expenses
- Reporting
- Client management
- Resource forecasting

Eliminates need to:
- Manually cross-reference Excel
- Manually update calendars
- Manually reconcile staffing vs timesheets
