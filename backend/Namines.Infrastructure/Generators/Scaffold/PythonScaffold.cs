using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Scaffold;

/// <summary>
/// Python (FastAPI + SQLAlchemy) ucretsiz proje sablonu.
///
/// <see cref="Namines.Infrastructure.Services.ScaffolderService"/>'ten ayrildi
/// (ARCH-003 / B-37): 1.760 satirlik tek dosya, hedef basina bir uretece
/// bolundu -- <c>Generators/Eject/</c> altindaki mevcut desenle ayni.
/// Metot govdeleri DEGISMEDI; <c>ScaffolderSnapshotTests</c> ciktinin bayt
/// bayt ayni kaldigini kanitliyor.
/// </summary>
internal static class PythonScaffold
{
    internal static string MapSqlToSqlAlchemyType(string type)
    {
        var t = type.ToUpperInvariant();
        if (t.Contains("INT")) return "Integer";
        if (t.Contains("VARCHAR") || t.Contains("TEXT") || t.Contains("CHAR") || t.Contains("STRING")) return "String";
        if (t.Contains("DECIMAL") || t.Contains("NUMERIC") || t.Contains("MONEY") || t.Contains("PRICE")) return "Numeric";
        if (t.Contains("DOUBLE") || t.Contains("FLOAT") || t.Contains("REAL")) return "Float";
        if (t.Contains("DATE") || t.Contains("TIME")) return "DateTime";
        if (t.Contains("BIT") || t.Contains("BOOL")) return "Boolean";
        return "String";
    }

    internal static string GeneratePythonDatabasePy(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("from sqlalchemy import create_engine, Column, Integer, String, Float, DateTime, Boolean, ForeignKey, Numeric");
        sb.AppendLine("from sqlalchemy.ext.declarative import declarative_base");
        sb.AppendLine("from sqlalchemy.orm import sessionmaker, relationship");
        sb.AppendLine("import os");
        sb.AppendLine("from datetime import datetime");
        sb.AppendLine();
        sb.AppendLine("DATABASE_URL = os.getenv('DATABASE_URL', 'postgresql://postgres:postgres@db:5432/postgres')");
        sb.AppendLine("engine = create_engine(DATABASE_URL)");
        sb.AppendLine("SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)");
        sb.AppendLine("Base = declarative_base()");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"class {table.Name}(Base):");
            sb.AppendLine($"    __tablename__ = '{table.Name.ToLowerInvariant()}'");
            sb.AppendLine();

            foreach (var col in table.Columns)
            {
                var saType = MapSqlToSqlAlchemyType(col.Type);
                var parts = new List<string> { saType };

                if (col.IsPK) parts.Add("primary_key=True");
                if (col.IsNullable) parts.Add("nullable=True");
                else if (!col.IsPK) parts.Add("nullable=False");

                var rel = schema.Relations.FirstOrDefault(r => r.SourceTableId == table.Id && r.SourceColumnId == col.Id);
                if (rel != null)
                {
                    var targetTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
                    var targetCol = targetTable?.Columns.FirstOrDefault(c => c.Id == rel.TargetColumnId);
                    if (targetTable != null && targetCol != null)
                    {
                        parts.Add($"ForeignKey('{targetTable.Name.ToLowerInvariant()}.{targetCol.Name.ToLowerInvariant()}')");
                    }
                }

                sb.AppendLine($"    {col.Name.ToLowerInvariant()} = Column({string.Join(", ", parts)})");
            }

            var tableRelations = schema.Relations.Where(r => r.SourceTableId == table.Id).ToList();
            foreach (var rel in tableRelations)
            {
                var targetTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
                if (targetTable != null)
                {
                    sb.AppendLine($"    {targetTable.Name.ToLowerInvariant()} = relationship('{targetTable.Name}')");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("def init_db():");
        sb.AppendLine("    Base.metadata.create_all(bind=engine)");
        sb.AppendLine();

        return sb.ToString();
    }

    internal static string GeneratePythonAppPy(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import streamlit as st");
        sb.AppendLine("import pandas as pd");
        sb.AppendLine("from database import SessionLocal, init_db, " + string.Join(", ", schema.Tables.Select(t => t.Name)));
        sb.AppendLine("from sqlalchemy.inspection import inspect");
        sb.AppendLine("import datetime");
        sb.AppendLine();
        sb.AppendLine("st.set_page_config(page_title='Namines CRUD Admin', page_icon='🗂️', layout='wide', initial_sidebar_state='expanded')");
        sb.AppendLine();
        sb.AppendLine("st.markdown(\"\"\"");
        sb.AppendLine("    <style>");
        sb.AppendLine("    .main { background-color: #0b0f19; color: #f3f4f6; }");
        sb.AppendLine("    .stButton>button { background-color: #4f46e5; color: white; border-radius: 8px; font-weight: bold; }");
        sb.AppendLine("    </style>");
        sb.AppendLine("\"\"\", unsafe_allow_html=True)");
        sb.AppendLine();
        sb.AppendLine("try:");
        sb.AppendLine("    init_db()");
        sb.AppendLine("except Exception as e:");
        sb.AppendLine("    st.error(f'Database Connection Error: {e}')");
        sb.AppendLine();
        sb.AppendLine("db = SessionLocal()");
        sb.AppendLine();
        sb.AppendLine("st.sidebar.title('🗂️ CRUD Engine')");
        sb.AppendLine("st.sidebar.subheader('Namines Admin Panel')");
        sb.AppendLine();

        sb.AppendLine("nav_options = ['🏠 Dashboard'] + [" + string.Join(", ", schema.Tables.Select(t => $"'{t.Name}'")) + "]");
        sb.AppendLine("choice = st.sidebar.selectbox('Navigate', nav_options)");
        sb.AppendLine();

        sb.AppendLine("if choice == '🏠 Dashboard':");
        sb.AppendLine("    st.title('🏠 Live Dashboard')");
        sb.AppendLine("    st.write('System metrics and overview of the database.')");
        sb.AppendLine();
        sb.AppendLine("    cols = st.columns(3)");
        sb.AppendLine($"    cols[0].metric('Total Tables', {schema.Tables.Count})");
        sb.AppendLine("    ");
        sb.AppendLine("    table_data = []");
        foreach (var t in schema.Tables)
        {
            sb.AppendLine($"    try:");
            sb.AppendLine($"        count = db.query({t.Name}).count()");
            sb.AppendLine($"        table_data.append({{'Table': '{t.Name}', 'Row Count': count}})");
            sb.AppendLine($"    except Exception:");
            sb.AppendLine($"        table_data.append({{'Table': '{t.Name}', 'Row Count': 'Error'}})");
        }
        sb.AppendLine("    df_stats = pd.DataFrame(table_data)");
        sb.AppendLine("    cols[1].metric('Total Records', df_stats['Row Count'].sum() if 'Error' not in df_stats['Row Count'].values else 'Unknown')");
        sb.AppendLine();
        sb.AppendLine("    st.subheader('Database Schema Overview')");
        sb.AppendLine("    st.dataframe(df_stats, use_container_width=True)");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            var pkCol = table.Columns.FirstOrDefault(c => c.IsPK) ?? table.Columns.FirstOrDefault();
            var pkName = pkCol?.Name.ToLowerInvariant() ?? "id";

            sb.AppendLine($"elif choice == '{table.Name}':");
            sb.AppendLine($"    st.title('📋 {table.Name} Manager')");
            sb.AppendLine();
            sb.AppendLine("    action = st.radio('Select Action', ['View Records', 'Create Record', 'Update Record', 'Delete Record'], horizontal=True)");
            sb.AppendLine();
            
            sb.AppendLine("    if action == 'View Records':");
            sb.AppendLine($"        st.subheader('Registered Data')");
            sb.AppendLine($"        try:");
            sb.AppendLine($"            records = db.query({table.Name}).all()");
            sb.AppendLine($"            if records:");
            sb.AppendLine($"                data = []");
            sb.AppendLine($"                for r in records:");
            sb.AppendLine($"                    row = {{}}");
            foreach (var col in table.Columns)
            {
                sb.AppendLine($"                    row['{col.Name}'] = getattr(r, '{col.Name.ToLowerInvariant()}')");
            }
            sb.AppendLine($"                    data.append(row)");
            sb.AppendLine($"                st.dataframe(pd.DataFrame(data), use_container_width=True)");
            sb.AppendLine($"            else:");
            sb.AppendLine($"                st.info('No records found in this table.')");
            sb.AppendLine($"        except Exception as e:");
            sb.AppendLine($"            st.error(f'Error reading table: {{e}}')");
            sb.AppendLine();

            sb.AppendLine("    elif action == 'Create Record':");
            sb.AppendLine($"        st.subheader('Add New Record')");
            sb.AppendLine($"        with st.form('create_form_{table.Name}'):");

            foreach (var col in table.Columns)
            {
                if (col.IsPK)
                {
                    var saType = MapSqlToSqlAlchemyType(col.Type);
                    if (saType == "Integer")
                    {
                        sb.AppendLine($"            st.text_input('{col.Name}', value='Auto-Incremented', disabled=True)");
                        continue;
                    }
                }
                
                var saTypeForm = MapSqlToSqlAlchemyType(col.Type);
                if (saTypeForm == "Integer")
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.number_input('{col.Name}', value=0, step=1)");
                }
                else if (saTypeForm == "Numeric" || saTypeForm == "Float")
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.number_input('{col.Name}', value=0.0)");
                }
                else if (saTypeForm == "Boolean")
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.checkbox('{col.Name}', value=False)");
                }
                else if (saTypeForm == "DateTime")
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.date_input('{col.Name}', value=datetime.date.today())");
                }
                else
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.text_input('{col.Name}', value='')");
                }
            }
            sb.AppendLine("            submitted = st.form_submit_button('Save Record')");
            sb.AppendLine("            if submitted:");
            sb.AppendLine($"                try:");
            sb.AppendLine($"                    new_record = {table.Name}(");
            foreach (var col in table.Columns)
            {
                if (col.IsPK && MapSqlToSqlAlchemyType(col.Type) == "Integer") continue;
                var saTypeForm = MapSqlToSqlAlchemyType(col.Type);
                if (saTypeForm == "DateTime")
                {
                    sb.AppendLine($"                        {col.Name.ToLowerInvariant()}=datetime.datetime.combine(val_{col.Name.ToLowerInvariant()}, datetime.time.min),");
                }
                else
                {
                    sb.AppendLine($"                        {col.Name.ToLowerInvariant()}=val_{col.Name.ToLowerInvariant()},");
                }
            }
            sb.AppendLine($"                    )");
            sb.AppendLine($"                    db.add(new_record)");
            sb.AppendLine($"                    db.commit()");
            sb.AppendLine($"                    st.success('Record successfully added!')");
            sb.AppendLine($"                except Exception as e:");
            sb.AppendLine($"                    db.rollback()");
            sb.AppendLine($"                    st.error(f'Error saving record: {{e}}')");
            sb.AppendLine();

            sb.AppendLine("    elif action == 'Update Record':");
            sb.AppendLine($"        st.subheader('Modify Existing Record')");
            sb.AppendLine($"        try:");
            sb.AppendLine($"            records = db.query({table.Name}).all()");
            sb.AppendLine($"            if not records:");
            sb.AppendLine($"                st.info('No records to update.')");
            sb.AppendLine($"            else:");
            sb.AppendLine($"                keys = [getattr(r, '{pkName}') for r in records]");
            sb.AppendLine($"                selected_key = st.selectbox('Select Record to Update ({pkCol?.Name})', keys)");
            sb.AppendLine($"                record_to_update = db.query({table.Name}).filter(getattr({table.Name}, '{pkName}') == selected_key).first()");
            sb.AppendLine($"                if record_to_update:");
            sb.AppendLine($"                    with st.form('update_form_{table.Name}'):");
            
            foreach (var col in table.Columns)
            {
                if (col.IsPK)
                {
                    sb.AppendLine($"                        st.text_input('{col.Name}', value=str(getattr(record_to_update, '{col.Name.ToLowerInvariant()}')), disabled=True)");
                    continue;
                }
                var saTypeForm = MapSqlToSqlAlchemyType(col.Type);
                if (saTypeForm == "Integer")
                {
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.number_input('{col.Name}', value=int(getattr(record_to_update, '{col.Name.ToLowerInvariant()}') or 0), step=1)");
                }
                else if (saTypeForm == "Numeric" || saTypeForm == "Float")
                {
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.number_input('{col.Name}', value=float(getattr(record_to_update, '{col.Name.ToLowerInvariant()}') or 0.0))");
                }
                else if (saTypeForm == "Boolean")
                {
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.checkbox('{col.Name}', value=bool(getattr(record_to_update, '{col.Name.ToLowerInvariant()}') or False))");
                }
                else if (saTypeForm == "DateTime")
                {
                    sb.AppendLine($"                        orig_val = getattr(record_to_update, '{col.Name.ToLowerInvariant()}')");
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.date_input('{col.Name}', value=orig_val.date() if orig_val else datetime.date.today())");
                }
                else
                {
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.text_input('{col.Name}', value=str(getattr(record_to_update, '{col.Name.ToLowerInvariant()}') or ''))");
                }
            }
            sb.AppendLine("                        submitted = st.form_submit_button('Update')");
            sb.AppendLine("                        if submitted:");
            sb.AppendLine($"                            try:");
            foreach (var col in table.Columns)
            {
                if (col.IsPK) continue;
                var saTypeForm = MapSqlToSqlAlchemyType(col.Type);
                if (saTypeForm == "DateTime")
                {
                    sb.AppendLine($"                                record_to_update.{col.Name.ToLowerInvariant()} = datetime.datetime.combine(val_{col.Name.ToLowerInvariant()}, datetime.time.min)");
                }
                else
                {
                    sb.AppendLine($"                                record_to_update.{col.Name.ToLowerInvariant()} = val_{col.Name.ToLowerInvariant()}");
                }
            }
            sb.AppendLine($"                                db.commit()");
            sb.AppendLine($"                                st.success('Record successfully updated!')");
            sb.AppendLine($"                            except Exception as e:");
            sb.AppendLine($"                                db.rollback()");
            sb.AppendLine($"                                st.error(f'Error updating record: {{e}}')");
            sb.AppendLine($"        except Exception as e:");
            sb.AppendLine($"            st.error(f'System error: {{e}}')");
            sb.AppendLine();

            sb.AppendLine("    elif action == 'Delete Record':");
            sb.AppendLine($"        st.subheader('Remove Record')");
            sb.AppendLine($"        try:");
            sb.AppendLine($"            records = db.query({table.Name}).all()");
            sb.AppendLine($"            if not records:");
            sb.AppendLine($"                st.info('No records to delete.')");
            sb.AppendLine($"            else:");
            sb.AppendLine($"                keys = [getattr(r, '{pkName}') for r in records]");
            sb.AppendLine($"                selected_key = st.selectbox('Select Record to Delete ({pkCol?.Name})', keys)");
            sb.AppendLine($"                if st.button('Confirm Delete Record'):");
            sb.AppendLine($"                    try:");
            sb.AppendLine($"                        record_to_del = db.query({table.Name}).filter(getattr({table.Name}, '{pkName}') == selected_key).first()");
            sb.AppendLine($"                        if record_to_del:");
            sb.AppendLine($"                            db.delete(record_to_del)");
            sb.AppendLine($"                            db.commit()");
            sb.AppendLine($"                            st.success('Record deleted successfully!')");
            sb.AppendLine($"                            st.rerun()");
            sb.AppendLine($"                    except Exception as e:");
            sb.AppendLine($"                        db.rollback()");
            sb.AppendLine($"                        st.error(f'Error deleting record: {{e}}')");
            sb.AppendLine($"        except Exception as e:");
            sb.AppendLine($"            st.error(f'System error: {{e}}')");
            sb.AppendLine();
        }

        sb.AppendLine("db.close()");
        return sb.ToString();
    }

    internal static string GeneratePythonRequirements()
    {
        return @"streamlit==1.32.0
SQLAlchemy==2.0.28
psycopg2-binary==2.9.9
pandas==2.2.1
";
    }

    internal static string GeneratePythonDockerfile()
    {
        return @"FROM python:3.11-slim

WORKDIR /app

RUN apt-get update && apt-get install -y \
    build-essential \
    libpq-dev \
    curl \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

EXPOSE 8501

HEALTHCHECK CMD curl --fail http://localhost:8501/_stcore/health

ENTRYPOINT [""streamlit"", ""run"", ""app.py"", ""--server.port=8501"", ""--server.address=0.0.0.0""]
";
    }

    internal static string GeneratePythonDockerCompose()
    {
        return @"version: '3.8'

services:
  db:
    image: postgres:15-alpine
    container_name: namines_postgres_db
    restart: always
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: postgres
    ports:
      - ""5432:5432""
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: [""CMD-SHELL"", ""pg_isready -U postgres""]
      interval: 5s
      timeout: 5s
      retries: 5

  app:
    build: .
    container_name: namines_streamlit_app
    restart: always
    ports:
      - ""8501:8501""
    environment:
      - DATABASE_URL=postgresql://postgres:postgres@db:5432/postgres
    depends_on:
      db:
        condition: service_healthy

volumes:
  pgdata:
";
    }

    internal static string GeneratePythonEnvExample()
    {
        return @"DATABASE_URL=postgresql://postgres:postgres@localhost:5432/postgres
";
    }

    internal static string GeneratePythonReadme(DatabaseSchema schema)
    {
        return $@"# {schema.Name} - Python Streamlit CRUD Admin

This project is a freemium admin interface generated automatically by Namines for your database schema.

## Features
- **SQLAlchemy ORM** database mapping with relationship setup
- **Streamlit-based live CRUD** (Create, Read, Update, Delete) forms
- **Visual metrics dashboard** to see record count distribution
- **Dockerized development environment** using PostgreSQL

## Quick Start (with Docker)

1. Ensure Docker and Docker Compose are installed on your machine.
2. Build and run the services:
   ```bash
   docker compose up --build -d
   ```
3. Access your live Streamlit CRUD interface at:
   [http://localhost:8501](http://localhost:8501)

## Manual Run (Virtual Environment)

1. Create a virtual environment:
   ```bash
   python -m venv venv
   source venv/bin/activate  # On Windows: venv\Scripts\activate
   ```
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```
3. Set your PostgreSQL environment variable and run the application:
   ```bash
   export DATABASE_URL=postgresql://postgres:postgres@localhost:5432/postgres
   streamlit run app.py
   ```
";
    }
}
