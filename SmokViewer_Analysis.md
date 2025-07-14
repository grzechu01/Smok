# SmokViewer Application Analysis

## Overview
SmokViewer is a Blazor Server application designed to monitor and visualize air quality and environmental measurements. The application displays real-time data through interactive charts and provides detailed tabular views of sensor readings.

## Architecture

### Technology Stack
- **Framework**: ASP.NET Core 6.0 with Blazor Server
- **Database**: SQL Server (Azure SQL Database)
- **Data Access**: Dapper ORM
- **Charting**: ChartJs.Blazor.Fork
- **UI Framework**: Bootstrap (implied from layout structure)

### Project Structure
```
SmokViewer/
├── Data/                    # Data layer components
│   ├── Measurement.cs       # Domain model for measurements
│   ├── MeasurementEntity.cs # Database entity
│   ├── MeasurementsDataSet.cs # Chart data structure
│   └── MeasurementsService.cs # Data access service
├── Pages/                   # Blazor pages
│   ├── Charts/              # Chart components
│   │   ├── LineChart.razor  # Interactive line chart component
│   │   ├── ChartModel.cs    # Chart configuration model
│   │   └── TimeScaleOptions.cs # Time filtering options
│   └── Index.razor          # Main dashboard page
├── Shared/                  # Shared components
│   ├── MainLayout.razor     # Main application layout
│   └── NavMenu.razor        # Navigation menu
└── wwwroot/                 # Static web assets
```

## Core Features

### 1. Environmental Data Monitoring
The application tracks multiple environmental parameters:
- **Temperature** (°C)
- **Humidity** (%)
- **Pressure** (hPa)
- **Particulate Matter**: PM1, PM2.5, PM10 (μg/m³)

### 2. Data Visualization
- **Interactive Charts**: Two separate line charts using Chart.js
  - Environmental data chart (Temperature, Humidity, Pressure)
  - Air quality chart (PM1, PM2.5, PM10)
- **Time Scale Filtering**: 24 hours, 3 days, 7 days
- **Responsive Design**: Charts adapt to screen size

### 3. Data Management
- **Real-time Updates**: Data is fetched from SQL Server database
- **Time Zone Handling**: Converts UTC timestamps to Central European Time
- **Data Aggregation**: Hourly averages for chart display
- **Historical Data**: Shows last 7 days of measurements

### 4. User Interface
- **Dashboard View**: Main page with charts and controls
- **Toggle Details**: Expandable table view for raw data
- **Radio Button Controls**: Time scale selection
- **Responsive Layout**: Mobile-friendly design

## Technical Implementation

### Database Schema
The application connects to a SQL Server database with a `Measurements` table containing:
- `Id`: Primary key
- `Timestamp`: DateTime (UTC)
- `Temperature`: Double
- `Humidity`: Double  
- `Pressure`: Double
- `PM1`: Double (Particulate Matter 1.0)
- `PM25`: Double (Particulate Matter 2.5)
- `PM10`: Double (Particulate Matter 10.0)

### Data Flow
1. **MeasurementsService** queries the database using Dapper
2. Raw data is filtered to last 7 days
3. **MeasurementEntity** objects are converted to **Measurement** domain models
4. Timestamps are converted from UTC to Central European Time
5. Data is passed to chart components for visualization
6. Charts aggregate data by hour and display time-series graphs

### Chart Configuration
- **Chart Type**: Line charts with time-based X-axis
- **Time Formatting**: "ll HH:mm" format for tooltips
- **Data Points**: Hourly averages with cubic interpolation
- **Color Scheme**: Predefined color palette (Red, Blue, Green, Purple, etc.)
- **Interactivity**: Hover tooltips and responsive scaling

### Data Processing
- **Filtering**: Time-based filtering (24h, 3d, 7d)
- **Aggregation**: Hourly averages using LINQ GroupBy
- **Rounding**: Values rounded to 2 decimal places for display

## Key Components

### MeasurementsService
```csharp
// Retrieves last 7 days of measurements
// Converts UTC to Central European Time
// Maps database entities to domain models
```

### LineChart Component
```razor
// Configurable line chart with time axis
// Supports multiple datasets
// Dynamic time scale filtering
// Hourly data aggregation
```

### Index Page
```razor
// Main dashboard with dual charts
// Time scale selection controls
// Toggle details functionality
// Data loading and initialization
```

## Configuration

### Database Connection
- **Server**: Azure SQL Database
- **Connection String**: Stored in appsettings.json
- **Authentication**: SQL Server authentication

### Time Zone Settings
- **Source**: UTC timestamps from database
- **Display**: Central European Standard Time
- **Conversion**: Automatic timezone conversion

## Color Scheme
The application uses a predefined color palette for data visualization:
- **Red**: #FF6384 (PM1)
- **Blue**: #36A2EB (PM2.5)
- **Green**: #4BC0C0 (Temperature)
- **Purple**: #9966FF (PM10)
- **Orange**: #FF9F40
- **Yellow**: #FFCD56
- **Grey**: #C9CBCF

## Performance Considerations
- **Data Limiting**: Only loads last 7 days of data
- **Hourly Aggregation**: Reduces data points for chart performance
- **Server-Side Rendering**: Blazor Server for fast initial load
- **Efficient Queries**: Dapper for lightweight data access

## Security Features
- **HTTPS**: Enforced in production
- **HSTS**: HTTP Strict Transport Security enabled
- **Connection Security**: Encrypted database connection
- **Input Validation**: Time scale validation

## Potential Improvements
1. **Real-time Updates**: WebSocket connections for live data
2. **Data Export**: CSV/Excel export functionality
3. **Alerts**: Threshold-based notifications
4. **Historical Analysis**: Trend analysis and forecasting
5. **Mobile App**: Native mobile application
6. **API**: REST API for external integrations
7. **Caching**: Redis caching for improved performance
8. **Error Handling**: Better error handling and logging

## Dependencies
- **ChartJs.Blazor.Fork**: Version 2.0.2 (Chart visualization)
- **Dapper**: Version 2.0.123 (Data access)
- **System.Data.SqlClient**: Version 4.8.3 (SQL Server connectivity)

## Conclusion
SmokViewer is a well-structured environmental monitoring application that effectively combines modern web technologies to provide real-time air quality visualization. The application demonstrates good separation of concerns, efficient data handling, and user-friendly interface design suitable for monitoring environmental conditions.